module Program

open Foundatio.Storage
open Converter
open System
open System.IO
open Pulumi.Experimental.Converter
open Pulumi.Codegen

let errorResponse (message: string) = 
    let diagnostics = ResizeArray [
        Diagnostic(Summary=message, Severity=DiagnosticSeverity.Error)
    ]
    
    ConvertProgramResponse(Diagnostics=diagnostics)

let logsFile() = Environment.GetEnvironmentVariable "BICEP_CONVERTER_LOGS"

let convertProgram (request: ConvertProgramRequest): ConvertProgramResponse =
    let logsFile = logsFile()
    if not (String.IsNullOrWhiteSpace logsFile) then
        let logs = ResizeArray [
            "Bicep Converter Request"
            "----------------------"
            "Source Directory: " + request.SourceDirectory
            "Target Directory: " + request.TargetDirectory
            "Args: " + String.Join(" ", request.Args)
        ]
        
        File.AppendAllLines(Path.Combine(request.SourceDirectory, logsFile), logs)

    let bicepFile =
       request.Args
       |> Seq.pairwise
       |> Seq.tryFind (fun (argKey, argValue) -> argKey = "--entry")
       |> Option.map (fun (_, entry) ->
           if not (entry.EndsWith ".bicep")
           then entry + ".bicep"
           else entry)
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

let convertProgramWithErrorHandling (request: ConvertProgramRequest): ConvertProgramResponse =
    try
        convertProgram request
    with
    | ex ->
        let logsFile = logsFile()
        if not (String.IsNullOrWhiteSpace logsFile) then
            File.AppendAllLines(Path.Combine(request.SourceDirectory, logsFile), [
                "Exception: " + ex.ToString()
            ])

        let diagnostics = ResizeArray [
            Diagnostic(Summary=ex.Message, Detail=ex.StackTrace, Severity=DiagnosticSeverity.Error)
        ]

        ConvertProgramResponse(Diagnostics=diagnostics)

convertProgramWithErrorHandling
|> Converter.CreateSimple
|> Converter.Serve
|> Async.AwaitTask
|> Async.RunSynchronously