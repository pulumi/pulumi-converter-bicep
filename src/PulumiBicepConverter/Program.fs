module Program

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.AspNetCore.Builder
open Pulumirpc
open Converter
open System.IO

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
    | Some file ->
        let content = File.ReadAllText(file)
        match BicepParser.parse content with
        | Error error ->
            return errorResponse $"failed to parse bicep file at {file}: {error}"
        | Ok bicepProgram ->
            let pulumiProgram =
                bicepProgram
                |> BicepProgram.simplifyScoping
                |> BicepProgram.parameterizeByResourceGroup
                |> Transform.bicepProgramToPulumi
                |> Printer.printProgram

            let targetFile = Path.Combine(request.TargetDirectory, "main.pp")
            File.WriteAllText(targetFile, pulumiProgram)
            return emptyResponse()
}

let convertState (request: ConvertStateRequest) = task {
    let response = ConvertStateResponse()
    return response
}

type BicepConverterService() = 
    inherit Converter.ConverterBase()
    override _.ConvertProgram(request, ctx) = convertProgram(request)
    override _.ConvertState(request, ctx) = convertState(request)

let freePort() =
    let listener = System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> System.Net.IPEndPoint).Port
    listener.Stop()
    port

let port = freePort()

// setup
let appBuilder = WebApplication.CreateBuilder()
appBuilder.Services.AddGrpc() |> ignore
appBuilder.WebHost.UseSetting("urls", $"http://localhost:{port}") |> ignore
appBuilder.Logging.ClearProviders() |> ignore

// write out the port so that Pulumi knows which port to connect to
printfn $"{port}"

// Run the application
let app = appBuilder.Build()
app.MapGrpcService<BicepConverterService>() |> ignore
app.Run()
