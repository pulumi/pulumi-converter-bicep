module Converter.Compile

open Foundatio.Storage
open System.IO
open BicepParser
open Humanizer

type CompilationArgs = {
    entryBicepSourceFile: string
    targetDirectory: string
    storage: IFileStorage
}

let compileProgramWithComponents (args: CompilationArgs) = task {
    let storage = args.storage
    let sourceDirectory = Path.GetDirectoryName args.entryBicepSourceFile
    let! bicepFileContent = storage.GetFileContentsAsync args.entryBicepSourceFile
    
    let rec compileProgram bicepProgram sourceDir targetDir targetFile isEntry = task {
        let rewriteComponentPath (path: string) =
            let fileName = Path.GetFileNameWithoutExtension path
            path.Replace($"{fileName}.bicep", fileName.Underscore())

        let pulumiProgram =
            if isEntry then 
                bicepProgram
                |> BicepProgram.simplifyScoping
                |> BicepProgram.addResourceGroupNameParameterToModules isEntry
                |> BicepProgram.parameterizeByResourceGroup
                |> Transform.bicepProgramToPulumi
                |> Transform.modifyComponentPaths rewriteComponentPath 
                |> Printer.printProgram
            else
                bicepProgram
                |> BicepProgram.addResourceGroupNameParameterToModules isEntry
                |> BicepProgram.parameterizeByResourceGroupName
                |> Transform.bicepProgramToPulumi
                |> Transform.modifyComponentPaths rewriteComponentPath
                |> Printer.printProgram

        let targetFile = Path.Combine(targetDir, targetFile)
        let! saved = storage.SaveFileAsync(targetFile, pulumiProgram)
    
        let moduleDeclarations =
            bicepProgram.declarations
            |> List.choose (function
                | BicepDeclaration.Module moduleDecl -> Some moduleDecl
                | _ -> None)
            |> List.distinctBy (fun moduleDecl -> moduleDecl.path)

        for moduleDecl in moduleDeclarations do
            let sourceModulePath = Path.Combine(sourceDir, moduleDecl.path)
            let! sourceModuleContent = storage.GetFileContentsAsync(sourceModulePath)
            let moduleName = (Path.GetFileNameWithoutExtension sourceModulePath).Underscore()
            let moduleProgram =
                match BicepParser.parse sourceModuleContent with
                | Error error -> { declarations = [] }
                | Ok program -> program

            let moduleTargetDir = Path.Combine(targetDir, moduleName)
            do! compileProgram moduleProgram sourceDir moduleTargetDir $"{moduleName}.pp" false
    }

    match BicepParser.parse bicepFileContent with
    | Error error ->
        return Error error
    | Ok mainBicepProgram ->
        let! result = compileProgram mainBicepProgram sourceDirectory args.targetDirectory "main.pp" true
        return Ok result
}