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
       request.Args
       |> Seq.pairwise
       |> Seq.tryFind (fun (argKey, argValue) -> argKey = "--entry")
       |> Option.map (fun (_, entry) ->
           if not (entry.EndsWith ".bicep")
           then entry
           else entry + ".bicep")
       |> Option.map (fun entryBicep ->
           if Path.IsPathRooted(entryBicep)
           then entryBicep
           else Path.Combine(request.SourceDirectory, entryBicep))
       |> Option.orElse (
           Directory.EnumerateFiles(request.SourceDirectory)
           |> Seq.tryFind (fun file -> Path.GetExtension(file) = ".bicep")
       )

    match bicepFile with
    | None -> 
        errorResponse "Could not find the entry bicep file from the source directory"
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