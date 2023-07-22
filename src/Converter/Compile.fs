module Converter.Compile

open Converter.BicepParser
open Foundatio.Storage
open System.IO
open BicepParser
open Humanizer

type CompilationArgs = {
    entryBicepSourceFile: string
    targetDirectory: string
    storage: IFileStorage
}

let compileProgramWithComponents (args: CompilationArgs) =
    let storage = args.storage
    let sourceDirectory = Path.GetDirectoryName args.entryBicepSourceFile
    let bicepFileContent =
        args.entryBicepSourceFile
        |> storage.GetFileContentsAsync 
        |> Async.AwaitTask
        |> Async.RunSynchronously
    
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
            storage.SaveFileAsync(targetFile, pulumiProgram)
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
                storage.GetFileContentsAsync(sourceModulePath)
                |> Async.AwaitTask
                |> Async.RunSynchronously
            let moduleName = (Path.GetFileNameWithoutExtension sourceModulePath).Underscore()
            let moduleProgram =
                match BicepParser.parse sourceModuleContent with
                | Error error -> { declarations = []; programKind = ProgramKind.Module }
                | Ok program -> { program with programKind = ProgramKind.Module }

            let moduleTargetDir = Path.Combine(targetDir, moduleName)
            compileProgram moduleProgram sourceDir moduleTargetDir $"{moduleName}.pp"

    match BicepParser.parse bicepFileContent with
    | Error error ->
        Error error
    | Ok mainBicepProgram ->
        let result = compileProgram mainBicepProgram sourceDirectory args.targetDirectory "main.pp"
        Ok result