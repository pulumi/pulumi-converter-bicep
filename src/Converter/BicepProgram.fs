module Converter.BicepProgram

open Bicep.Core.Exceptions
open BicepParser
open Converter.BicepParser

let tryFindParameter (name: string) (program: BicepProgram) =
    program.declarations
    |> Seq.tryFind(function 
        | BicepDeclaration.Parameter param when name = param.name -> true        
        | _ -> false)
    |> function
        | Some (BicepDeclaration.Parameter param) -> Some param
        | _ -> None

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
        
let findModule name program =
    match tryFindModule name program with
    | Some foundModule -> foundModule
    | None -> failwith $"Couldn't find module with name '{name}'"

let isModuleDeclaration (name: string) (program: BicepProgram) =
    match tryFindModule name program with
    | Some foundModule -> true
    | None -> false

let findResource (name: string) (program: BicepProgram) =
    match tryFindResource name program with
    | Some resource -> resource
    | None -> failwith $"Couldn't find resource with name {name}"

let rec replaceVariables (replacements: Map<string, BicepSyntax>) (rootExpression: BicepSyntax) =
    match rootExpression with
    | BicepSyntax.VariableAccess variableName ->
        match Map.tryFind variableName replacements with
        | Some replacement -> replacement
        | None -> rootExpression

    | BicepSyntax.Array items -> BicepSyntax.Array [ for item in items -> replaceVariables replacements item ]

    | BicepSyntax.Object properties ->
        BicepSyntax.Object(Map.ofList [
            for key, value in Map.toList properties do
                replaceVariables replacements key, replaceVariables replacements value
        ])

    | BicepSyntax.FunctionCall (name, args) ->
        BicepSyntax.FunctionCall(name, [ for arg in args -> replaceVariables replacements arg ])

    | BicepSyntax.PropertyAccess (target, property) ->
        BicepSyntax.PropertyAccess (replaceVariables replacements target, property)
       
    | BicepSyntax.IndexExpression (target, index) ->
        BicepSyntax.IndexExpression (replaceVariables replacements target, replaceVariables replacements index)
        
    | BicepSyntax.UnaryExpression (op, expression) ->
        BicepSyntax.UnaryExpression (op, replaceVariables replacements expression)
        
    | BicepSyntax.BinaryExpression (op, left, right) ->
        let left = replaceVariables replacements left
        let right = replaceVariables replacements right
        BicepSyntax.BinaryExpression (op, left, right)

    | BicepSyntax.TernaryExpression (condition, trueResult, falseResult) ->
        let condition = replaceVariables replacements condition
        let trueResult = replaceVariables replacements trueResult
        let falseResult = replaceVariables replacements falseResult
        BicepSyntax.TernaryExpression (condition, trueResult, falseResult)
        
    | BicepSyntax.InterpolatedString  (expressions, segments) ->
        let expressions = [for expr in expressions -> replaceVariables replacements expr]
        BicepSyntax.InterpolatedString (expressions, segments)

    | _ ->
        rootExpression