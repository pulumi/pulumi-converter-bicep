module Converter.Compile

open Bicep.Core.Syntax
open Converter.BicepParser
open Foundatio.Storage
open System.IO
open BicepParser
open Humanizer

[<RequireQualifiedAccess>]
type BicepSource =
    | Content of string
    | Program of ProgramSyntax
    | FilePath of string

type CompilationArgs = {
    entryBicepSource: BicepSource
    sourceDirectory: string
    targetDirectory: string
    storage: IFileStorage
}

let compileProgramWithComponents (args: CompilationArgs) =
    let rec compileProgram bicepProgram sourceDir targetDir targetFile = 
        let rewriteComponentPath (path: string) =
            let fileName = Path.GetFileNameWithoutExtension path
            path.Replace($"{fileName}.bicep", fileName.Underscore())

        let pulumiProgram =
            if bicepProgram.programKind = ProgramKind.EntryPoint then 
                bicepProgram
                |> BicepProgram.reduceScopeParameter
                |> BicepProgram.parameterizeByTenantId
                |> BicepProgram.addResourceGroupNameParameterToModules
                |> BicepProgram.parameterizeByResourceGroup
                |> Transform.bicepProgramToPulumi
                |> Transform.modifyComponentPaths rewriteComponentPath 
                |> Printer.printProgram
            else
                bicepProgram
                |> BicepProgram.reduceScopeParameter
                |> BicepProgram.parameterizeByTenantId
                |> BicepProgram.addResourceGroupNameParameterToModules
                |> BicepProgram.parameterizeByResourceGroupName
                |> Transform.bicepProgramToPulumi
                |> Transform.modifyComponentPaths rewriteComponentPath
                |> Printer.printProgram

        let targetFile = Path.Combine(targetDir, targetFile)
        let saved =
            args.storage.SaveFileAsync(targetFile, pulumiProgram)
            |> Async.AwaitTask
            |> Async.RunSynchronously
    
        let moduleDeclarations =
            bicepProgram.declarations
            |> List.choose (function
                | BicepDeclaration.Module moduleDecl -> Some moduleDecl
                | _ -> None)
            |> List.distinctBy (fun moduleDecl -> moduleDecl.path)

        for moduleDecl in moduleDeclarations do
            let sourceModulePath = Path.Combine(sourceDir, moduleDecl.path)
            let sourceModuleContent =
                args.storage.GetFileContentsAsync(sourceModulePath)
                |> Async.AwaitTask
                |> Async.RunSynchronously
            let moduleName = (Path.GetFileNameWithoutExtension sourceModulePath).Underscore()
            let moduleProgram =
                match BicepParser.parse sourceModuleContent with
                | Error error -> { declarations = []; programKind = ProgramKind.Module }
                | Ok program -> { program with programKind = ProgramKind.Module }

            let moduleTargetDir = Path.Combine(targetDir, moduleName)
            compileProgram moduleProgram sourceDir moduleTargetDir $"{moduleName}.pp"

    let mainBicepProgram =
        match args.entryBicepSource with
        | BicepSource.Content bicepFileContent -> BicepParser.parse bicepFileContent
        | BicepSource.Program programSyntax -> BicepParser.parseProgram programSyntax
        | BicepSource.FilePath path ->
            path
            |> args.storage.GetFileContentsAsync 
            |> Async.AwaitTask
            |> Async.RunSynchronously
            |> BicepParser.parse
   
    match mainBicepProgram with
    | Error error ->
        Error error

    | Ok program ->
        let result = compileProgram program args.sourceDirectory args.targetDirectory "main.pp"
        Ok result