module Program

open Foundatio.Storage
open Converter
open System.IO
open Pulumi.Experimental.Converter
open Pulumi.Codegen

let errorResponse (message: string) = 
    let diagnostics = ResizeArray [
        Diagnostic(Summary=message, Severity=DiagnosticSeverity.Error)
    ]
    
    ConvertProgramResponse(Diagnostics=diagnostics)

let convertProgram (request: ConvertProgramRequest): ConvertProgramResponse =
    let bicepFile =
       Directory.EnumerateFiles(request.SourceDirectory)
       |> Seq.tryFind (fun file -> Path.GetExtension(file) = ".bicep")

    match bicepFile with
    | None -> 
        errorResponse "No Bicep file found in the source directory"
    | Some entryBicepFile ->
        let storageOptions = FolderFileStorageOptions(Folder=request.SourceDirectory)
        let storage = new FolderFileStorage(storageOptions)
        let result = Compile.compileProgramWithComponents {
            entryBicepSource = Compile.BicepSource.FilePath entryBicepFile
            sourceDirectory = request.SourceDirectory
            targetDirectory = request.TargetDirectory
            storage = storage
        }

        match result with
        | Error error -> errorResponse error
        | Ok() -> ConvertProgramResponse.Empty

convertProgram
|> Converter.CreateSimple
|> Converter.Serve
|> Async.AwaitTask
|> Async.RunSynchronously