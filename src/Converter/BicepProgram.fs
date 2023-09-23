module Converter.BicepProgram

open Bicep.Core.Exceptions
open Bicep.Core.TypeSystem
open Bicep.Core.TypeSystem.Az
open BicepParser
open Converter
open Converter.BicepParser

let tryFindParameter (name: string) (program: BicepProgram) =
    program.declarations
    |> Seq.tryFind(function 
        | BicepDeclaration.Parameter param when name = param.name -> true        
        | _ -> false)
    |> function
        | Some (BicepDeclaration.Parameter param) -> Some param
        | _ -> None
        
let tryFindVariable (name: string) (program: BicepProgram) =
    program.declarations
    |> Seq.tryFind(function 
        | BicepDeclaration.Variable variable when name = variable.name -> true        
        | _ -> false)
    |> function
        | Some (BicepDeclaration.Variable variable) -> Some variable
        | _ -> None
        
let findVariable (name: string) (program: BicepProgram) =
    match tryFindVariable name program with
    | Some variable -> variable
    | None -> failwith $"Couldn't find variable {name}"

let findParameter (name: string) (program: BicepProgram) =
    match tryFindParameter name program with
    | Some param -> param
    | None -> failwith $"Couldn't find parameter with name {name}"

let tryFindResource (name: string) (program: BicepProgram) =
    program.declarations
    |> Seq.tryFind(function 
        | BicepDeclaration.Resource resource when name = resource.name -> true        
        | _ -> false)
    |> function
        | Some (BicepDeclaration.Resource resource) -> Some resource
        | _ -> None

let tryFindModule (name: string) (program: BicepProgram) =
    program.declarations
    |> Seq.tryFind(function 
        | BicepDeclaration.Module bicepModule when name = bicepModule.name -> true        
        | _ -> false)
    |> function
        | Some (BicepDeclaration.Module bicepModule) -> Some bicepModule
        | _ -> None
        
let tryFindOutput (name: string) (program: BicepProgram) =
    program.declarations
    |> Seq.tryFind(function 
        | BicepDeclaration.Output output when name = output.name -> true        
        | _ -> false)
    |> function
        | Some (BicepDeclaration.Output output) -> Some output
        | _ -> None

let findOutput (name: string) (program: BicepProgram) =
    match tryFindOutput name program with
    | None -> failwith $"failed to find output with name {name}"
    | Some output -> output
        
let findModule name program =
    match tryFindModule name program with
    | Some foundModule -> foundModule
    | None -> failwith $"Couldn't find module with name '{name}'"

let isModuleDeclaration (name: string) (program: BicepProgram) =
    match tryFindModule name program with
    | Some foundModule -> true
    | None -> false

let isResourceDeclaration (name: string) (program: BicepProgram) =
    match tryFindResource name program with
    | Some resource -> true
    | None ->
        match tryFindVariable name program with
        | None -> false
        | Some variable ->
            match variable.value with
            | BicepSyntax.FunctionCall("getExistingResource", _) -> true
            | _ -> false

let findResource (name: string) (program: BicepProgram) =
    match tryFindResource name program with
    | Some resource -> resource
    | None -> failwith $"Couldn't find resource with name {name}"

let rec replace (replacements: Map<BicepSyntax, BicepSyntax>) (rootExpression: BicepSyntax) =
    match Map.tryFind rootExpression replacements with
    | Some foundReplacement -> foundReplacement
    | None -> 
        match rootExpression with
        | BicepSyntax.Array items -> BicepSyntax.Array [ for item in items -> replace replacements item ]

        | BicepSyntax.Object properties ->
            BicepSyntax.Object(Map.ofList [
                for key, value in Map.toList properties do
                    replace replacements key, replace replacements value
            ])

        | BicepSyntax.FunctionCall (name, args) ->
            BicepSyntax.FunctionCall(name, [ for arg in args -> replace replacements arg ])

        | BicepSyntax.PropertyAccess (target, property) ->
            BicepSyntax.PropertyAccess (replace replacements target, property)
           
        | BicepSyntax.IndexExpression (target, index) ->
            BicepSyntax.IndexExpression (replace replacements target, replace replacements index)
            
        | BicepSyntax.UnaryExpression (op, expression) ->
            BicepSyntax.UnaryExpression (op, replace replacements expression)
            
        | BicepSyntax.BinaryExpression (op, left, right) ->
            let left = replace replacements left
            let right = replace replacements right
            BicepSyntax.BinaryExpression (op, left, right)

        | BicepSyntax.TernaryExpression (condition, trueResult, falseResult) ->
            let condition = replace replacements condition
            let trueResult = replace replacements trueResult
            let falseResult = replace replacements falseResult
            BicepSyntax.TernaryExpression (condition, trueResult, falseResult)

        | BicepSyntax.InterpolatedString  (expressions, segments) ->
            let expressions = [for expr in expressions -> replace replacements expr]
            BicepSyntax.InterpolatedString (expressions, segments)
            
        | BicepSyntax.IfCondition (condition, body) ->
            let condition = replace replacements condition
            let body = replace replacements body
            BicepSyntax.IfCondition(condition, body)
            
        | BicepSyntax.For forSyntax ->
            let expression = replace replacements forSyntax.expression
            let body = replace replacements forSyntax.body
            BicepSyntax.For { forSyntax with expression = expression; body = body }

        | _ ->
            rootExpression

let rec contains (predicate: BicepSyntax -> bool) (rootExpression: BicepSyntax) =
    if predicate rootExpression then
        true
    else
        match rootExpression with
        | BicepSyntax.Array items ->
            List.exists (contains predicate) items

        | BicepSyntax.Object properties ->
            properties
            |> Map.values
            |> Seq.exists (contains predicate)

        | BicepSyntax.FunctionCall (name, args) ->
            List.exists (contains predicate) args

        | BicepSyntax.PropertyAccess (target, property) ->
            contains predicate target
           
        | BicepSyntax.IndexExpression (target, index) ->
            contains predicate target || contains predicate index
            
        | BicepSyntax.UnaryExpression (op, expression) ->
            contains predicate expression
            
        | BicepSyntax.BinaryExpression (op, left, right) ->
            contains predicate left || contains predicate right

        | BicepSyntax.TernaryExpression (condition, trueResult, falseResult) ->
            contains predicate condition
                || contains predicate trueResult
                || contains predicate falseResult

        | BicepSyntax.InterpolatedString  (expressions, segments) ->
            List.exists (contains predicate) expressions

        | _ ->
            false

let replaceVariables (variables: Map<string, BicepSyntax>) (rootExpression: BicepSyntax) =
    let replacements = Map.ofList [
        for key, value in Map.toList variables do
            BicepSyntax.VariableAccess key, value
    ]

    replace replacements rootExpression

let programContains (predicate: BicepSyntax -> bool) (bicepProgram: BicepProgram) =
    bicepProgram.declarations
    |> List.exists (function
        | BicepDeclaration.Parameter param -> Option.exists (contains predicate) param.defaultValue
        | BicepDeclaration.Variable variable -> contains predicate variable.value
        | BicepDeclaration.Output output -> contains predicate output.value
        | BicepDeclaration.Resource resource -> contains predicate resource.value
        | BicepDeclaration.Module moduleDecl -> contains predicate moduleDecl.value)

let programReplace (replacements: Map<BicepSyntax, BicepSyntax>) (bicepProgram: BicepProgram) =
    let modifiedDeclarations =
        bicepProgram.declarations
        |> List.map (function
            | BicepDeclaration.Parameter param ->
                let defaultValue = param.defaultValue |> Option.map (replace replacements)
                BicepDeclaration.Parameter { param with defaultValue = defaultValue  }
                    
            | BicepDeclaration.Variable variable ->
                let value = replace replacements variable.value
                BicepDeclaration.Variable { variable with value = value }
                
            | BicepDeclaration.Output output ->
                let value = replace replacements output.value
                BicepDeclaration.Output { output with value = value }
                
            | BicepDeclaration.Resource resource ->
                let value = replace replacements resource.value
                BicepDeclaration.Resource { resource with value = value }
                
            | BicepDeclaration.Module moduleDecl ->
                let value = replace replacements moduleDecl.value
                BicepDeclaration.Module { moduleDecl with value = value })
        
    { bicepProgram with declarations = modifiedDeclarations }

let reduceScopeParameter (bicepProgram: BicepProgram) : BicepProgram =
    let rewriteResourceScopeProperty properties token =
        let modifiedProperties =
            if bicepProgram.programKind = ProgramKind.Module then
                // when inside modules, remove specialized scoping
                properties
                |> Map.remove (BicepSyntax.Identifier Tokens.Scope)
            else
            Map.ofList [
                for key, value in Map.toList properties do 
                    match key with
                    | BicepSyntax.Identifier Tokens.Scope ->
                        match value with
                        // { scope: resourceGroup() }
                        // becomes
                        // { resourceGroupName: resourceGroup().name }
                        | BicepSyntax.FunctionCall(Tokens.ResourceGroup, []) ->
                            let newKey = BicepSyntax.Identifier Tokens.ResourceGroupName
                            let newValue = BicepSyntax.PropertyAccess(value, "name")
                            yield newKey, newValue
                        // { scope: resourceGroup(name) }
                        // becomes
                        // { resourceGroupName: name }
                        | BicepSyntax.FunctionCall(Tokens.ResourceGroup, [ resourceGroupName ]) ->
                            let newKey = BicepSyntax.Identifier Tokens.ResourceGroupName
                            let newValue = resourceGroupName
                            yield newKey, newValue
                        | BicepSyntax.VariableAccess referenceToResourceGroup ->
                            match tryFindVariable referenceToResourceGroup bicepProgram with
                            | Some reference ->
                                match reference.value with
                                | BicepSyntax.FunctionCall(Tokens.ResourceGroup, _) ->
                                    let newKey = BicepSyntax.Identifier Tokens.ResourceGroupName
                                    let newValue = BicepSyntax.PropertyAccess(value, "name")
                                    yield newKey, newValue
        
                                | _ ->
                                    // reference to anything else other than resourceGroup(...)
                                    ()
                            | None ->
                                // couldn't find the reference, remove the property
                                ()
                        | _ ->
                            // any other assignment is removed
                            ()
                    | _ -> yield key, value
           ]
        
        // Now that handled the scope property
        // if a resource didn't have any scope, add a resourceGroupName parameter to it
        let pulumiToken = ResourceTokens.fromAzureSpecToPulumiWithoutVersion token
        if Array.contains pulumiToken Schema.resourcesWhichRequireResourceGroupName then
            let resourceGroupName = BicepSyntax.Identifier Tokens.ResourceGroupName
            if not (Map.containsKey resourceGroupName modifiedProperties) then
               
               let resourceGroupValue =
                   match bicepProgram.programKind with
                   | ProgramKind.Module ->
                       // when inside modules, expect a parameter called resourceGroupName
                       BicepSyntax.VariableAccess Tokens.ResourceGroupName

                   | ProgramKind.EntryPoint ->
                       // when inside the main program, access the "ambient" resource group
                       // via resourceGroup().name
                       BicepSyntax.PropertyAccess(BicepSyntax.FunctionCall(Tokens.ResourceGroup, [  ]), "name")

               modifiedProperties
               |> Map.add resourceGroupName resourceGroupValue
               |> BicepSyntax.Object
            else
               BicepSyntax.Object modifiedProperties      
        else 
            BicepSyntax.Object modifiedProperties

    let modifiedDeclarations =
        bicepProgram.declarations
        |> List.map (function
            | BicepDeclaration.Resource resource ->
                match resource.value with
                | BicepSyntax.Object properties ->
                    let modifiedProperties = rewriteResourceScopeProperty properties resource.token
                    BicepDeclaration.Resource { resource with value = modifiedProperties }

                | BicepSyntax.IfCondition (condition, BicepSyntax.Object properties) ->
                    let modifiedProperties = rewriteResourceScopeProperty properties resource.token
                    let modifiedIfCondition = BicepSyntax.IfCondition(condition, modifiedProperties)
                    BicepDeclaration.Resource { resource with value = modifiedIfCondition }

                | BicepSyntax.For forSyntax ->
                    match forSyntax.body with
                    | BicepSyntax.Object properties ->
                        let modifiedProperties = rewriteResourceScopeProperty properties resource.token
                        let modifiedFor = BicepSyntax.For { forSyntax with body = modifiedProperties }
                        BicepDeclaration.Resource { resource with value =  modifiedFor }
                        
                    | _ -> BicepDeclaration.Resource resource
                    
                | _ -> BicepDeclaration.Resource resource
                
            | BicepDeclaration.Variable variable ->
                match variable.value with
                // apply scoping to existing resource declarations
                | BicepSyntax.FunctionCall(Tokens.GetExistingResource, [ BicepSyntax.String token; BicepSyntax.Object properties ]) ->
                    let modifiedProperties = rewriteResourceScopeProperty properties token
                    let modifiedValue = BicepSyntax.FunctionCall(Tokens.GetExistingResource, [
                        BicepSyntax.String token
                        modifiedProperties
                    ])
                    
                    BicepDeclaration.Variable { variable with value = modifiedValue }
                    
                | _ -> BicepDeclaration.Variable variable
                
            | declaration -> declaration)
        
    { bicepProgram with declarations = modifiedDeclarations  }

let parameterizeByResourceGroup (bicepProgram: BicepProgram) : BicepProgram =
    let containsImplicitResourceGroup =
        bicepProgram
        |> programContains (function
            | BicepSyntax.FunctionCall("resourceGroup", [  ]) -> true
            | _ -> false)

    let resourceGroupNameParameter = BicepDeclaration.Parameter {
        name = "resourceGroupName"
        parameterType = Some "string"
        defaultValue = None
        decorators = [
            BicepSyntax.FunctionCall("description", [
                BicepSyntax.String "The name of the resource group to operate on"
            ])
        ]
    }
    
    let explicitResourceGroupVariable = BicepDeclaration.Variable {
        name = "currentResourceGroup"
        value = BicepSyntax.FunctionCall("resourceGroup", [
            BicepSyntax.VariableAccess "resourceGroupName"
        ])
    }
    
    if not containsImplicitResourceGroup then
        bicepProgram
    else
        let replacements = Map.ofList [
            BicepSyntax.FunctionCall("resourceGroup", [  ]), BicepSyntax.VariableAccess "currentResourceGroup"
        ]

        let modifiedProgram = programReplace replacements bicepProgram
        let programWithAddedDeclarations = {
            bicepProgram with
                declarations = [
                    match tryFindParameter "resourceGroupName" modifiedProgram  with
                    | None ->
                        // add the parameter
                        yield resourceGroupNameParameter
                    | _ ->
                        ()
                    yield explicitResourceGroupVariable
                    yield! modifiedProgram.declarations
                ]
        }
        
        programWithAddedDeclarations

let parameterizeByTenantAndSubscriptionId (bicepProgram: BicepProgram) : BicepProgram =
    let containsImplicitSubscriptionTenantId =
        bicepProgram
        |> programContains (function
            | BicepSyntax.PropertyAccess(BicepSyntax.FunctionCall("subscription", [  ]), "tenantId") -> true
            | _ -> false)
    
    let containsImplicitTenantId =
        bicepProgram
        |> programContains (function
            | BicepSyntax.PropertyAccess(BicepSyntax.FunctionCall("tenant", [  ]), "tenantId") -> true
            | _ -> false)

    let containsImplicitSubscriptionId =
        bicepProgram
        |> programContains (function
            | BicepSyntax.PropertyAccess(BicepSyntax.FunctionCall("subscription", [  ]), "subscriptionId") -> true
            | _ -> false)
    
    if not containsImplicitSubscriptionTenantId && not containsImplicitTenantId && not containsImplicitSubscriptionId then
        bicepProgram
    else
        let currentClientConfig = BicepDeclaration.Variable {
            name = "currentClientConfig"
            value = BicepSyntax.FunctionCall("getClientConfig", [  ])
        }

        let replacements = Map.ofList [
            BicepSyntax.PropertyAccess(BicepSyntax.FunctionCall("subscription", [  ]), "tenantId"),
                BicepSyntax.PropertyAccess(BicepSyntax.VariableAccess "currentClientConfig", "tenantId")
                
            BicepSyntax.PropertyAccess(BicepSyntax.FunctionCall("subscription", [  ]), "subscriptionId"),
                BicepSyntax.PropertyAccess(BicepSyntax.VariableAccess "currentClientConfig", "subscriptionId")
            
            BicepSyntax.PropertyAccess(BicepSyntax.FunctionCall("tenant", [  ]), "tenantId"),
                BicepSyntax.PropertyAccess(BicepSyntax.VariableAccess "currentClientConfig", "tenantId")
        ]

        let modifiedProgram = programReplace replacements bicepProgram
        let programWithAddedDeclarations = {
            bicepProgram with
                declarations = [
                    yield currentClientConfig
                    yield! modifiedProgram.declarations
                ]
        }

        programWithAddedDeclarations
        
let parameterizeByResourceGroupName (bicepProgram: BicepProgram) : BicepProgram =
    let resourceGroupNameParameter = BicepDeclaration.Parameter {
        name = Tokens.ResourceGroupName
        parameterType = Some "string"
        defaultValue = None
        decorators = [
            BicepSyntax.FunctionCall("description", [
                BicepSyntax.String "The name of the resource group to operate on"
            ])
        ]
    }

    {
      bicepProgram with 
          declarations = [
            match tryFindVariable Tokens.ResourceGroupName bicepProgram with
            | None -> yield resourceGroupNameParameter
            | Some _ -> ()
            
            yield! bicepProgram.declarations
          ]
    }

let addResourceGroupNameParameterToModules (bicepProgram: BicepProgram)  : BicepProgram =
    let addResourceGroupName' properties =
       let resourceGroupName = BicepSyntax.Identifier Tokens.ResourceGroupName
       if not (Map.containsKey resourceGroupName properties) then
          let resourceGroupNameValue =
              if bicepProgram.programKind = ProgramKind.EntryPoint then
                  BicepSyntax.PropertyAccess(BicepSyntax.FunctionCall(Tokens.ResourceGroup, [ ]), "name")
              else
                  BicepSyntax.VariableAccess Tokens.ResourceGroupName

          properties
          |> Map.add resourceGroupName resourceGroupNameValue
          |> BicepSyntax.Object
       else
          BicepSyntax.Object properties      

    let addResourceGroupName properties =
        match Map.tryFind (BicepSyntax.Identifier "params") properties with
        | Some (BicepSyntax.Object actualProperties) ->
            let modified = addResourceGroupName' actualProperties
            properties
            |> Map.remove (BicepSyntax.Identifier "params")
            |> Map.add (BicepSyntax.Identifier "params") modified
            |> BicepSyntax.Object

        | _ ->
           BicepSyntax.Object properties
  
    let modifiedDeclarations =
        bicepProgram.declarations
        |> List.map (function
            | BicepDeclaration.Module moduleDecl ->
                match moduleDecl.value with
                | BicepSyntax.Object properties ->
                    let modifiedProperties = addResourceGroupName properties
                    BicepDeclaration.Module { moduleDecl with value = modifiedProperties }

                | BicepSyntax.IfCondition (condition, BicepSyntax.Object properties) ->
                    let modifiedProperties = addResourceGroupName properties
                    let modifiedIfCondition = BicepSyntax.IfCondition(condition, modifiedProperties)
                    BicepDeclaration.Module { moduleDecl with value = modifiedIfCondition }

                | BicepSyntax.For forSyntax ->
                    match forSyntax.body with
                    | BicepSyntax.Object properties ->
                        let modifiedProperties = addResourceGroupName properties
                        let modifiedFor = BicepSyntax.For { forSyntax with body = modifiedProperties }
                        BicepDeclaration.Module { moduleDecl with value =  modifiedFor }
                        
                    | _ -> BicepDeclaration.Module moduleDecl
                    
                | _ -> BicepDeclaration.Module moduleDecl
                
            | declaration -> declaration)

    { bicepProgram with declarations = modifiedDeclarations }
 
let azureResourceTypes =
    let loader = AzResourceTypeLoader()
    let provider = AzResourceTypeProvider(loader)
    let references = provider.GetAvailableTypes()
    Map.ofList [
        for ref in references do
            let fullTypeName = String.concat "/" ref.TypeSegments
            $"{fullTypeName}@{ref.ApiVersion}", ref
    ]

[<RequireQualifiedAccess>]
type AzureType =
    | Any
    | Object of azureProperties: Map<string, AzureType>
    | Array of AzureType

let private dropUnknownProperties (token: string) (resourceBody: Map<BicepSyntax, BicepSyntax>) : BicepSyntax =
    let rec dropUnknowns (azureType: AzureType) (bicepValue: BicepSyntax) =
        match azureType with
        | AzureType.Any ->
            bicepValue
        | AzureType.Object azureProperties -> 
            match bicepValue with
            | BicepSyntax.Object bicepProperties ->
                let modifiedProperties = ResizeArray<BicepSyntax * BicepSyntax>()
                for property in bicepProperties do
                    match property.Key with
                    | BicepSyntax.Identifier identifier ->
                        match Map.tryFind identifier azureProperties with
                        | None ->
                            // couldn't find the property, drop it
                            ()
                        | Some azureProperty ->
                            let modifiedValue = dropUnknowns azureProperty property.Value
                            modifiedProperties.Add (property.Key, modifiedValue)
                    | _ ->
                        modifiedProperties.Add (property.Key, property.Value)
                    
                BicepSyntax.Object (Map.ofSeq modifiedProperties)
                
            | _ -> bicepValue

        | AzureType.Array elementType ->
            match bicepValue with
            | BicepSyntax.Array elements ->
                BicepSyntax.Array [ for element in elements -> dropUnknowns elementType element ]
            | _ ->
                bicepValue

    let rec createAzureType (typeRef: ITypeReference) : AzureType =
        match typeRef.Type with
        | :? ObjectType as objectType when objectType.Properties.Count > 0 ->
            AzureType.Object(Map.ofList [
                for property in objectType.Properties do
                    yield property.Key, createAzureType property.Value.TypeReference
            ])

        | :? ArrayType as arrayType ->
            AzureType.Array (createAzureType arrayType.Item)
            
        | _ ->
            AzureType.Any

    match Map.tryFind token azureResourceTypes with
    | None -> BicepSyntax.Object resourceBody
    | Some typeRef ->
        let inputProperties = AzResourceTypeProvider.CreateResourceProperties typeRef
        let azureProperties = AzureType.Object(Map.ofList [
            for prop in inputProperties do
                yield prop.Name, createAzureType prop.TypeReference
                
            yield "parent", AzureType.Any
        ])
        
        dropUnknowns azureProperties (BicepSyntax.Object resourceBody)

let dropResourceUnknowns (bicepProgram: BicepProgram)  : BicepProgram =
    let modifiedDeclarations =
        bicepProgram.declarations
        |> List.map (function
            | BicepDeclaration.Resource resource ->
                match resource.value with
                | BicepSyntax.Object properties ->
                    let modifiedProperties = dropUnknownProperties resource.token properties 
                    BicepDeclaration.Resource { resource with value = modifiedProperties }

                | BicepSyntax.IfCondition (condition, BicepSyntax.Object properties) ->
                    let modifiedProperties = dropUnknownProperties resource.token properties 
                    let modifiedIfCondition = BicepSyntax.IfCondition(condition, modifiedProperties)
                    BicepDeclaration.Resource { resource with value = modifiedIfCondition }

                | BicepSyntax.For forSyntax ->
                    match forSyntax.body with
                    | BicepSyntax.Object properties ->
                        let modifiedProperties = dropUnknownProperties resource.token properties
                        let modifiedFor = BicepSyntax.For { forSyntax with body = modifiedProperties }
                        BicepDeclaration.Resource { resource with value =  modifiedFor }
                        
                    | _ -> BicepDeclaration.Resource resource
                    
                | _ -> BicepDeclaration.Resource resource
                
            | declaration -> declaration)
        
    { bicepProgram with declarations = modifiedDeclarations  }