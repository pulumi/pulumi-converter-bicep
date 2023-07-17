module Program

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.AspNetCore.Builder
open Pulumirpc

let convertProgram (request: ConvertProgramRequest) = task {
    let response = ConvertProgramResponse()
    return response
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
