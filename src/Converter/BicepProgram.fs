module Converter.BicepProgram

open BicepParser

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
        
let isModuleDeclaration (name: string) (program: BicepProgram) =
    match tryFindModule name program with
    | Some foundModule -> true
    | None -> false

let findResource (name: string) (program: BicepProgram) =
    match tryFindResource name program with
    | Some resource -> resource
    | None -> failwith $"Couldn't find resource with name {name}"