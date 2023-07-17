module rec Converter.Transform

open Converter.BicepParser
open Converter.PulumiTypes

let invoke (token: string) (args: (string * PulumiSyntax) list) =
    let args = PulumiSyntax.Object(Map.ofList [
        for (key, value) in args -> PulumiSyntax.String key, value
    ])
    
    let token = PulumiSyntax.String token
    PulumiSyntax.FunctionCall("invoke", [token; args])

let standardFunction (name: string) args =
    PulumiSyntax.PropertyAccess(invoke $"std:index:{name}" args, "result")

let transformFunction name args program =
    match name, args with
    | "resourceGroup", arg :: _ ->
        invoke "azure-native:resources:getResourceGroup" [
            "resourceGroupName", fromBicep arg program
        ]

    | "loadloadFileAsBase64", arg :: _ -> standardFunction "filebase64" [ "input", fromBicep arg program ]
    | "loadTextContent", arg :: _ -> standardFunction "file" [ "input", fromBicep arg program ]

    | funcName, args ->
        PulumiSyntax.FunctionCall(funcName, [ for arg in args -> fromBicep arg program ])

let rec fromBicep (expr: BicepSyntax) (program: BicepProgram) =
    match expr with
    | BicepSyntax.String value -> PulumiSyntax.String value
    | BicepSyntax.Integer value -> PulumiSyntax.Integer value
    | BicepSyntax.Boolean value -> PulumiSyntax.Boolean value
    | BicepSyntax.Null -> PulumiSyntax.Null
    | BicepSyntax.Array values -> PulumiSyntax.Array [ for value in values -> fromBicep value program ]
    | BicepSyntax.Object properties ->
        PulumiSyntax.Object(Map.ofList [
            for (key, value) in Map.toList properties do
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
        PulumiSyntax.For {
            expression = fromBicep forSyntax.expression program
            body = fromBicep forSyntax.body program
            variables =
                match forSyntax.variableSection with
                | BicepSyntax.LocalVariable name -> [ name ]
                | BicepSyntax.LocalVariableBlock names -> names
                | anythingElse -> []
        }

    | _ -> PulumiSyntax.Empty

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

let extractResourceInputs (properties: Map<BicepSyntax, BicepSyntax>) (program: BicepProgram) =
    let reservedResourceKeywords = ["name"; "scope"; "parent"; "dependsOn"]
    Map.ofList [
        for (propertyName, propertyValue) in Map.toList properties do
            match propertyName with
            | BicepSyntax.Identifier "odata.type" ->
                yield "odataType", fromBicep propertyValue program
            | BicepSyntax.Identifier propertyName ->
                if not (List.contains propertyName reservedResourceKeywords) then
                    yield propertyName, fromBicep propertyValue program
            | _ ->
                ()
    ]

let extractLogicalName (properties: Map<BicepSyntax, BicepSyntax>) =
    let name = properties |> Map.tryFind (BicepSyntax.Identifier "name")
    match name with
    | Some (BicepSyntax.String logicalName) -> Some logicalName
    | _ -> None

let bicepResource (resource: ResourceDeclaration) (program: BicepProgram) : Resource option =
    let name = resource.name
    let resourceType = ResourceTokens.fromAzureSpecToPulumi resource.token
    match resource.value with
    | BicepSyntax.Object properties ->
        let resource : Resource = {
            name = name
            token = resourceType
            logicalName = extractLogicalName properties
            inputs = extractResourceInputs properties program
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
            inputs = extractResourceInputs properties program
            options = Some resourceOptions
        }
        
        Some resource

    | BicepSyntax.For forLoopExpression ->
        None
    | _ ->
        None
        

