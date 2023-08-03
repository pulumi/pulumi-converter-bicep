module rec Converter.Transform

open System.IO
open Converter.BicepParser
open Converter.PulumiTypes
open Foundatio.Storage

let invoke (token: string) (args: (string * PulumiSyntax) list) =
    let args = PulumiSyntax.Object(Map.ofList [
        for (key, value) in args -> PulumiSyntax.Identifier key, value
    ])
    
    let token = PulumiSyntax.String token
    PulumiSyntax.FunctionCall("invoke", [token; args])

let standardFunction (name: string) args =
    PulumiSyntax.PropertyAccess(invoke $"std:index:{name}" args, "result")

let notImplemented (errorMessage: string) =
    PulumiSyntax.FunctionCall("notImplemented", [ PulumiSyntax.String errorMessage ])
    
let transformFunction name args program =
    match name, args with
    | "resourceGroup", arg :: _ ->
        invoke "azure-native:resources:getResourceGroup" [
            "resourceGroupName", fromBicep arg program
        ]

    | "getExistingResource", [ BicepSyntax.String resourceToken; BicepSyntax.Object properties ] ->
        let invokeToken = ResourceTokens.fromAzureSpecToExistingResourceToken resourceToken
        invoke invokeToken [
            for key, value in Map.toList properties do
                match key with
                | BicepSyntax.Identifier "name" ->
                    match Map.tryFind invokeToken Schema.nameParameterForExistingResources with
                    | Some specificNameParameter ->
                        yield specificNameParameter, fromBicep value program
                    | None -> ()
                | BicepSyntax.Identifier other ->
                    yield other, fromBicep value program
                | _ -> ()
        ]

    | "loadloadFileAsBase64", arg :: _ -> standardFunction "filebase64" [ "input", fromBicep arg program ]
    | "loadTextContent", arg :: _ -> standardFunction "file" [ "input", fromBicep arg program ]
    | "json", arg :: _ -> PulumiSyntax.FunctionCall("toJSON", [ fromBicep arg program ])
    | "reference", arg :: _ -> notImplemented "TODO: reference(...) is not available yet"
    | "toLower", arg :: _ -> standardFunction "lower" [ "input", fromBicep arg program ]
    | "toUpper", arg :: _ -> standardFunction "upper" [ "input", fromBicep arg program ]
    | "startsWith", [ input; prefix ] ->
        standardFunction "startswith" [
            "input", fromBicep input program
            "prefix", fromBicep prefix program
        ]
    
    | "endsWith", [ input; suffix ] ->
        standardFunction "endswith" [
            "input", fromBicep input program
            "suffix", fromBicep suffix program
        ]
    
    | "base64", arg :: _ -> standardFunction "base64encode" [ "input", fromBicep arg program ]
    | "base64ToString", arg :: _ -> standardFunction "base64decode" [ "input", fromBicep arg program ]
    | "array", args -> PulumiSyntax.Array [ for arg in args -> fromBicep arg program ]
    
    | funcName, args ->
        PulumiSyntax.FunctionCall(funcName, [ for arg in args -> fromBicep arg program ])

let transformForExpression (expr: BicepForSyntax) (program: BicepProgram) : PulumiForSyntax =
    let valueVariableReplacement =
        match expr.variableSection with
        | BicepSyntax.LocalVariable variableName ->
            Some (variableName, BicepSyntax.PropertyAccess(BicepSyntax.VariableAccess "entry", "value"))
        | BicepSyntax.LocalVariableBlock (variableName::_) ->
            Some (variableName, BicepSyntax.PropertyAccess(BicepSyntax.VariableAccess "entry", "value"))
        | _ -> None

    let keyVariableReplacement =
        match expr.variableSection with
        | BicepSyntax.LocalVariableBlock [ _; variableName] ->
            Some (variableName, BicepSyntax.PropertyAccess(BicepSyntax.VariableAccess "entry", "key"))
        | _ -> None

    let replacements = List.choose id [
        valueVariableReplacement
        keyVariableReplacement
    ]

    let modifiedBody = BicepProgram.replaceVariables (Map.ofList replacements) expr.body
    let pulumiForExpression = {
        variable = "entry"
        expression = PulumiSyntax.FunctionCall("entries", [ fromBicep expr.expression program ])
        body = fromBicep modifiedBody program
    }
    
    pulumiForExpression
    
let rec fromBicep (expr: BicepSyntax) (program: BicepProgram) =
    match expr with
    | BicepSyntax.String value -> PulumiSyntax.String value
    | BicepSyntax.InterpolatedString (expressions, segments) ->
        let expressions = [ for value in expressions -> fromBicep value program ]
        PulumiSyntax.InterpolatedString (expressions, segments)
    | BicepSyntax.Integer value -> PulumiSyntax.Integer value
    | BicepSyntax.Boolean value -> PulumiSyntax.Boolean value
    | BicepSyntax.Null -> PulumiSyntax.Null
    | BicepSyntax.Array values -> PulumiSyntax.Array [ for value in values -> fromBicep value program ]
    | BicepSyntax.Object properties ->
        PulumiSyntax.Object(Map.ofList [
            for (key, value) in Map.toList properties do
                match key with
                | BicepSyntax.String "odata.type" ->
                    PulumiSyntax.Identifier "odataType", fromBicep value program
                | otherwise ->
                    fromBicep key program, fromBicep value program
        ])
  
    | BicepSyntax.TernaryExpression(condition, trueValue, falseValue) ->
        let condition = fromBicep condition program
        let trueValue = fromBicep trueValue program
        let falseValue = fromBicep falseValue program
        PulumiSyntax.TernaryExpression(condition, trueValue, falseValue)

    | BicepSyntax.BinaryExpression (op, left, right) ->
        let left = fromBicep left program
        let right = fromBicep right program
        PulumiSyntax.BinaryExpression(op, left, right)

    | BicepSyntax.UnaryExpression (op, value) ->
        let value = fromBicep value program
        PulumiSyntax.UnaryExpression(op, value)

    | BicepSyntax.FunctionCall(("uniqueId" | "uniqueString"), [ arg ]) ->
        // reduce away `uniqueId` and `uniqueString` functions because it is used for naming resources
        // pulumi already handles unique resource names without it
        fromBicep arg program

    | BicepSyntax.FunctionCall (name, args) ->
        transformFunction name args program

    | BicepSyntax.PropertyAccess (BicepSyntax.VariableAccess variableName, "outputs") ->
        if BicepProgram.isModuleDeclaration variableName program then
            // reduce {module.outputs} to just {module}
            PulumiSyntax.VariableAccess variableName
        else
            // translate as is
            let variableAccess = fromBicep (BicepSyntax.VariableAccess variableName) program
            PulumiSyntax.PropertyAccess(variableAccess, "outputs")

    | BicepSyntax.PropertyAccess (BicepSyntax.VariableAccess variableName, "properties") ->
        if BicepProgram.isResourceDeclaration variableName program then
            // reduce {resource.properties} to just {resource}
            PulumiSyntax.VariableAccess variableName
        else
            // translate as is
            let variableAccess = fromBicep (BicepSyntax.VariableAccess variableName) program
            PulumiSyntax.PropertyAccess(variableAccess, "properties")

    | BicepSyntax.PropertyAccess (target, property) ->
        PulumiSyntax.PropertyAccess(fromBicep target program, property)
        
    | BicepSyntax.VariableAccess variable ->
        match BicepProgram.tryFindResource variable program with
        | Some resource ->
            match resource.value with
            | BicepSyntax.IfCondition _ ->
                // conditional resource R becomes R[0]
                PulumiSyntax.IndexExpression(
                    PulumiSyntax.VariableAccess variable,
                    PulumiSyntax.Integer 0)
            | _ ->
                PulumiSyntax.VariableAccess variable
        | None ->
            PulumiSyntax.VariableAccess variable
            
    | BicepSyntax.Identifier "odata.type" ->
        PulumiSyntax.Identifier "odataType"
        
    | BicepSyntax.Identifier identifier ->
        PulumiSyntax.Identifier identifier

    | BicepSyntax.For forSyntax ->
        let transformed = transformForExpression forSyntax program
        PulumiSyntax.For transformed

    | BicepSyntax.IndexExpression (target, index) ->
        let target = fromBicep target program
        let index = fromBicep index program
        PulumiSyntax.IndexExpression (target, index)
        
    | BicepSyntax.IfCondition (condition, value) ->
        // this is only used for conditional resources
        // change it into a ternary expression
        let rewritten = BicepSyntax.TernaryExpression(condition, value, BicepSyntax.Null)
        fromBicep rewritten program
    
    | _ ->
        PulumiSyntax.Empty

let rec inferPulumiType (defaultValue: BicepSyntax) =
    match defaultValue with
    | BicepSyntax.String _ -> "string"
    | BicepSyntax.InterpolatedString _ -> "string"
    | BicepSyntax.Integer _ -> "int"
    | BicepSyntax.Boolean _ -> "bool"
    | BicepSyntax.Array items when List.isEmpty items -> "list(any)"
    | BicepSyntax.Array items ->
        let elementTypes =
            items
            |> List.map inferPulumiType
            |> List.distinct

        match elementTypes with
        | [ elementType ] -> $"list({elementType})"
        | _ -> "list(any)"

    | BicepSyntax.Object properties when Map.isEmpty properties -> "map(any)"
    | BicepSyntax.Object properties ->
        let elementTypes =
            properties
            |> Map.values
            |> Seq.map inferPulumiType
            |> Seq.distinct
            |> Seq.toList

        match elementTypes with
        | [ elementType ] -> $"map({elementType})"
        | _ ->
            let elementTypes =
                properties
                |> Map.toList
                |> List.sortBy fst
                |> List.collect (fun (key, value) ->
                    match key with
                    | BicepSyntax.Identifier key -> [key, inferPulumiType value]
                    | BicepSyntax.String key -> [key, inferPulumiType value]
                    | _ -> [])
                |> List.map (fun (key, inferredType) -> $"{key}={inferredType}")
                |> String.concat ", "

            sprintf "object({%s})" elementTypes
    | _ -> "any"

let inferConfigType (bicepParam: ParameterDeclaration) =
    match bicepParam.parameterType with
    | Some "array" ->
        match bicepParam.defaultValue with
        | Some defaultValue -> inferPulumiType defaultValue
        | None -> "list(any)"
    | Some "object" ->
        match bicepParam.defaultValue with
        | Some defaultValue -> inferPulumiType defaultValue
        | None -> "map(any)"
    | Some paramType -> paramType
    | None -> "any"

let bicepParameterToConfig (bicepParam: ParameterDeclaration) (program: BicepProgram) : ConfigVariable =
    {
        description = bicepParam.description
        name = bicepParam.name
        defaultValue = Option.map (fun value -> fromBicep value program) bicepParam.defaultValue
        configType = inferConfigType bicepParam
    }
    
let bicepVariable (variableDeclaration: VariableDeclaration) (program: BicepProgram) : LocalVariable =
    {
        name = variableDeclaration.name
        value = fromBicep variableDeclaration.value program
    }
    
let bicepOutput (output: OutputDeclaration) (program: BicepProgram) : OutputVariable =
    {
        name = output.name
        value = fromBicep output.value program
    }

let extractResourceOptions (properties: Map<BicepSyntax, BicepSyntax>) (program: BicepProgram) =
    let find key =
        match Map.tryFind (BicepSyntax.Identifier key) properties with
        | Some value -> Some (key, value)
        | None -> None

    let rec loop options acc =
        match options with
        | ("dependsOn", value) :: rest ->
            match acc with
            | None ->
                let resourceOptions = ResourceOptions.Empty
                let modifiedOptions = { resourceOptions with dependsOn = Some (fromBicep value program) }
                loop rest (Some modifiedOptions)

            | Some resourceOptions ->
                let modifiedOptions = { resourceOptions with dependsOn = Some (fromBicep value program) }
                loop rest (Some modifiedOptions)

        | ("parent", value) :: rest ->
            match acc with
            | None ->
                let resourceOptions = ResourceOptions.Empty
                let modifiedOptions = { resourceOptions with parent = Some (fromBicep value program) }
                loop rest (Some modifiedOptions)
                
            | Some resourceOptions ->
                let modifiedOptions = { resourceOptions with parent = Some (fromBicep value program) }
                loop rest (Some modifiedOptions)
                
        | _ -> acc
    
    let options = List.choose id [find "dependsOn"; find "parent"]
    loop options None

let extractResourceInputs (resourceToken: string) (properties: Map<BicepSyntax, BicepSyntax>) (program: BicepProgram) =
    let reservedResourceKeywords = ["name"; "scope"; "parent"; "dependsOn"]
    Map.ofList [
        for (propertyName, propertyValue) in Map.toList properties do
            match propertyName with
            | BicepSyntax.String "odata.type" ->
                yield "odataType", fromBicep propertyValue program
            | BicepSyntax.Identifier "name" ->
                match Map.tryFind resourceToken Schema.nameParameterForResources with
                | None -> ()
                | Some parameterName ->
                    yield parameterName, fromBicep propertyValue program
            | BicepSyntax.Identifier propertyName ->
                if not (List.contains propertyName reservedResourceKeywords) then
                    yield propertyName, fromBicep propertyValue program
            | _ ->
                ()
    ]
    
let spreadProperties token (inputs: Map<string, PulumiSyntax>): Map<string, PulumiSyntax> =
    if not (Array.contains token Schema.resourcesWhichHaveInputPropertiesObject) then
        // for resources which don't have a property called "properties"
        // spread the properties into the first level of attributes for that resource
        Map.ofList [
            for key, value in Map.toList inputs do
                match key, value with
                | "properties", PulumiSyntax.Object properties ->
                    for propertyName, propertyValue in Map.toList properties do
                        match propertyName with
                        | PulumiSyntax.Identifier name -> 
                            yield name, propertyValue
                        | PulumiSyntax.String name ->
                            yield name, propertyValue
                        | _ ->
                            ()
                | _ ->
                    yield key, value
        ]
    else
        // if a resource does have a "properties" property
        // leave it as is
        inputs

let extractComponentInputs (properties: Map<string, PulumiSyntax>) =
    match Map.tryFind "params" properties with
    | Some(PulumiSyntax.Object componentInputs) -> componentInputs
    | _ -> Map.empty

let extractLogicalName (properties: Map<BicepSyntax, BicepSyntax>) =
    let name = properties |> Map.tryFind (BicepSyntax.Identifier "name")
    match name with
    | Some (BicepSyntax.String logicalName) -> Some logicalName
    | _ -> None

let bicepResource (resource: ResourceDeclaration) (program: BicepProgram) : Resource option =
    let name = resource.name
    let resourceType = ResourceTokens.fromAzureSpecToPulumiWithoutVersion resource.token
    match resource.value with
    | BicepSyntax.Object properties ->
        let resource : Resource = {
            name = name
            token = resourceType
            logicalName = extractLogicalName properties
            inputs =
                extractResourceInputs resourceType properties program
                |> spreadProperties resourceType
            options = extractResourceOptions properties program
        }

        Some resource

    | BicepSyntax.IfCondition (condition, BicepSyntax.Object properties) ->
        // range = condition ? 1 : 0
        let range = PulumiSyntax.TernaryExpression(
            fromBicep condition program,
            PulumiSyntax.Integer 1,
            PulumiSyntax.Integer 0)

        let resourceOptions =
            extractResourceOptions properties program
            |> function
                | Some options -> { options with range = Some range }
                | None -> { ResourceOptions.Empty with range = Some range }

        let resource : Resource = {
            name = name
            token = resourceType
            logicalName = extractLogicalName properties
            inputs =
                extractResourceInputs resourceType properties program
                |> spreadProperties resourceType
            options = Some resourceOptions
        }
        
        Some resource

    | BicepSyntax.For forLoopExpression ->
        let valueVariable =
            match forLoopExpression.variableSection with
            | BicepSyntax.LocalVariable name -> Some name
            | BicepSyntax.LocalVariableBlock names when names.Length > 0 -> Some names[0]
            | _ -> None

        let keyVariable =
            match forLoopExpression.variableSection with
            | BicepSyntax.LocalVariableBlock names when names.Length = 2 -> Some names[1]
            | _ -> None

        let properties =
            let valueReplacement = BicepSyntax.PropertyAccess(BicepSyntax.VariableAccess "range", "value")
            let keyReplacement = BicepSyntax.PropertyAccess(BicepSyntax.VariableAccess "range", "key")

            let replacements = Map.ofList [
                match valueVariable with
                | Some variableName -> variableName, valueReplacement
                | None -> ()
                
                match keyVariable with
                | Some variableName -> variableName, keyReplacement
                | None -> ()
            ]
            
            let replacedBody = BicepProgram.replaceVariables replacements forLoopExpression.body
            match replacedBody with
            | BicepSyntax.Object modifiedProperties -> modifiedProperties
            | _ -> Map.empty
            
        let createResource range =
            let resourceOptions =
                extractResourceOptions properties program
                |> function
                    | Some options -> { options with range = Some (fromBicep range program) }
                    | None -> { ResourceOptions.Empty with range = Some (fromBicep range program) }

            let resource : Resource = {
                name = name
                token = resourceType
                logicalName = extractLogicalName properties
                inputs =
                    extractResourceInputs resourceType properties program
                    |> spreadProperties resourceType
                options = Some resourceOptions
            }

            Some resource
   
        match keyVariable, forLoopExpression.expression with
        | None, BicepSyntax.FunctionCall("range", [ BicepSyntax.Integer start; count ]) when start = 0 ->
            // transform [ for index in range(0, count): { ... } ] to options { range = count }
            createResource count
        | _ ->
            // transform [ for <vars> in <expr>: { ... } ] to options { range = <expr> }
            createResource  forLoopExpression.expression
    | _ ->
        None


let bicepModule (moduleDecl: ModuleDeclaration) (program: BicepProgram) : Component option =
    match moduleDecl.value with
    | BicepSyntax.Object properties ->
        let componentDecl : Component = {
            name = moduleDecl.name
            path = moduleDecl.path
            logicalName = extractLogicalName properties
            inputs =
                extractResourceInputs "" properties program
                |> extractComponentInputs
            options = extractResourceOptions properties program
        }

        Some componentDecl

    | BicepSyntax.IfCondition (condition, BicepSyntax.Object properties) ->
        // range = condition ? 1 : 0
        let range = PulumiSyntax.TernaryExpression(
            fromBicep condition program,
            PulumiSyntax.Integer 1,
            PulumiSyntax.Integer 0)

        let resourceOptions =
            extractResourceOptions properties program
            |> function
                | Some options -> { options with range = Some range }
                | None -> { ResourceOptions.Empty with range = Some range }

        let componentDecl : Component = {
            name = moduleDecl.name
            path = moduleDecl.path
            logicalName = extractLogicalName properties
            inputs =
                extractResourceInputs "" properties program
                |> extractComponentInputs
            options = Some resourceOptions
        }
        
        Some componentDecl

    | BicepSyntax.For forLoopExpression ->
        let valueVariable =
            match forLoopExpression.variableSection with
            | BicepSyntax.LocalVariable name -> Some name
            | BicepSyntax.LocalVariableBlock names when names.Length > 0 -> Some names[0]
            | _ -> None

        let keyVariable =
            match forLoopExpression.variableSection with
            | BicepSyntax.LocalVariableBlock names when names.Length = 2 -> Some names[1]
            | _ -> None

        let properties =
            let valueReplacement = BicepSyntax.PropertyAccess(BicepSyntax.VariableAccess "range", "value")
            let keyReplacement = BicepSyntax.PropertyAccess(BicepSyntax.VariableAccess "range", "key")

            let replacements = Map.ofList [
                match valueVariable with
                | Some variableName -> variableName, valueReplacement
                | None -> ()
                
                match keyVariable with
                | Some variableName -> variableName, keyReplacement
                | None -> ()
            ]
            
            let replacedBody = BicepProgram.replaceVariables replacements forLoopExpression.body
            match replacedBody with
            | BicepSyntax.Object modifiedProperties -> modifiedProperties
            | _ -> Map.empty
            
        let createComponentDecl range =
            let resourceOptions =
                extractResourceOptions properties program
                |> function
                    | Some options -> { options with range = Some (fromBicep range program) }
                    | None -> { ResourceOptions.Empty with range = Some (fromBicep range program) }

            let componentDecl : Component = {
                name = moduleDecl.name
                path = moduleDecl.path
                logicalName = extractLogicalName properties
                inputs =
                    extractResourceInputs "" properties program
                    |> extractComponentInputs
                options = Some resourceOptions
            }
            
            Some componentDecl
   
        match keyVariable, forLoopExpression.expression with
        | None, BicepSyntax.FunctionCall("range", [ BicepSyntax.Integer start; count ]) when start = 0 ->
            // transform [ for index in range(0, count): { ... } ] to options { range = count }
            createComponentDecl count
        | _ ->
            // transform [ for <vars> in <expr>: { ... } ] to options { range = <expr> }
            createComponentDecl  forLoopExpression.expression
    | _ ->
        None

let bicepProgramToPulumi (bicepProgram: BicepProgram) : PulumiProgram =
    let nodes = [
         for declaration in bicepProgram.declarations do
         match declaration with
         | BicepDeclaration.Parameter parameter ->
             let configVar = bicepParameterToConfig parameter bicepProgram
             PulumiNode.ConfigVariable configVar
             
         | BicepDeclaration.Variable variable ->
             let localVariable = bicepVariable variable bicepProgram
             PulumiNode.LocalVariable localVariable
             
         | BicepDeclaration.Output output ->
             let outputVariable = bicepOutput output bicepProgram
             PulumiNode.OutputVariable outputVariable
             
         | BicepDeclaration.Resource resource ->
             match bicepResource resource bicepProgram with
             | Some resource -> PulumiNode.Resource resource
             | None -> ()

         | BicepDeclaration.Module moduleDecl ->
             match bicepModule moduleDecl bicepProgram with
             | Some componentDecl -> PulumiNode.Component componentDecl
             | None -> ()
    ]
    
    { nodes = nodes }
    
    
let findPulumiVariable (name: string) (program: PulumiProgram) =
    program.nodes
    |> List.tryFind (function
        | PulumiNode.LocalVariable variable -> name = variable.name
        | _ -> false)
    |> function
        | Some (PulumiNode.LocalVariable variable) -> variable
        | _ -> failwith $"Failed to find variable {name}"

let modifyComponentPaths (map: string -> string) (program: PulumiProgram) =
    let modifiedNodes =
        program.nodes
        |> List.map (function
            | PulumiNode.Component componentDecl ->
                PulumiNode.Component { componentDecl with path = map componentDecl.path }
            | declaration -> declaration)

    { nodes = modifiedNodes }
    