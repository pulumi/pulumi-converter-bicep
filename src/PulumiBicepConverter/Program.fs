module Program

open System.Threading
open Converter.BicepParser
open Foundatio.Storage
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.AspNetCore.Builder
open Pulumirpc
open Converter
open System.IO
open PulumiConverterPlugin

let errorResponse (message: string) = 
    let response = ConvertProgramResponse()
    let errorDiagnostic = Codegen.Diagnostic()
    errorDiagnostic.Summary <- message
    errorDiagnostic.Severity <- Codegen.DiagnosticSeverity.DiagError
    response.Diagnostics.Add(errorDiagnostic)
    response

let emptyResponse() = ConvertProgramResponse()

let convertProgram (request: ConvertProgramRequest) = task {
    let bicepFile =
       Directory.EnumerateFiles(request.SourceDirectory)
       |> Seq.tryFind (fun file -> Path.GetExtension(file) = ".bicep")

    match bicepFile with
    | None -> 
        return errorResponse "No Bicep file found in the source directory"
    | Some entryBicepFile ->
        let storageOptions = FolderFileStorageOptions(Folder=request.SourceDirectory)
        let storage = new FolderFileStorage(storageOptions)
        let result = Compile.compileProgramWithComponents {
            entryBicepSourceFile = entryBicepFile
            targetDirectory = request.TargetDirectory
            storage = storage
        }
        
        match result with
        | Error error -> return errorResponse error
        | Ok() -> return emptyResponse()
}

let convertState (request: ConvertStateRequest) = task {
    let response = ConvertStateResponse()
    return response
}

type BicepConverterService() = 
    inherit Converter.ConverterBase()
    override _.ConvertProgram(request, ctx) = convertProgram(request)
    override _.ConvertState(request, ctx) = convertState(request)

let serve args =
    let cancellationToken = CancellationToken()
    PulumiConverterPlugin.Serve<BicepConverterService>(args, cancellationToken, System.Console.Out)
    |> Async.AwaitTask
    |> Async.RunSynchronously

[<EntryPoint>]
let main(args: string[]) =
    serve args
    0