open System
open System.IO
open System.Linq
open System.Threading.Tasks
open Fake.IO
open Fake.Core
open Publish
open CliWrap
open CliWrap.Buffered
open System.Text

/// Recursively tries to find the parent of a file starting from a directory
let rec findParent (directory: string) (fileToFind: string) =
    let path = if Directory.Exists(directory) then directory else Directory.GetParent(directory).FullName
    let files = Directory.GetFiles(path)
    if files.Any(fun file -> Path.GetFileName(file).ToLower() = fileToFind.ToLower())
    then path
    else findParent (DirectoryInfo(path).Parent.FullName) fileToFind

let repositoryRoot = findParent __SOURCE_DIRECTORY__ "README.md";

let syncProtoFiles() = GitSync.repository {
    remoteRepository = "https://github.com/pulumi/pulumi.git"
    localRepositoryPath = repositoryRoot
    contents = [
        GitSync.folder {
            sourcePath = [ "proto"; "pulumi" ]
            destinationPath = [ "proto"; "pulumi" ]
        }

        GitSync.folder {
            sourcePath = [ "proto"; "google"; "protobuf" ]
            destinationPath = [ "proto"; "google"; "protobuf" ]
        }
    ]
}

let pulumiCliBinary() : Task<string> = task {
    try
        // try to get the version of pulumi installed on the system
        let! version =
            Cli.Wrap("pulumi")
                .WithArguments("version")
                .WithValidation(CommandResultValidation.ZeroExitCode)
                .ExecuteAsync()

        return "pulumi"
    with
    | _ ->
        // when pulumi is not installed, try to get the version of of the dev build
        // installed on the system using `make install` in the pulumi repo
        let homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        let pulumiPath = System.IO.Path.Combine(homeDir, ".pulumi-dev", "bin", "pulumi")
        if System.IO.File.Exists pulumiPath then
            return pulumiPath
        elif System.IO.File.Exists $"{pulumiPath}.exe" then
            return $"{pulumiPath}.exe"
        else
            return "pulumi"
}

let schemaFromPulumi(pluginName: string) = task {
    let packageName = $"{pluginName}@2.0.0"
    let! binary = pulumiCliBinary()
    let! output =
         Cli.Wrap(binary)
            .WithArguments($"package get-schema {packageName}")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync()

    if output.ExitCode <> 0 then
        return Error output.StandardError
    else
        return Ok output.StandardOutput
}

let parseSchemaFromPulumi(pluginName: string) =
    task {
        let! schema = schemaFromPulumi(pluginName)
        return
            match schema with
            | Ok schema -> Ok (PulumiSchema.Parser.parseSchema schema)
            | Error error -> Error error
    }
    |> Async.AwaitTask
    |> Async.RunSynchronously


let runTests() =
    if Shell.Exec("dotnet", "run", Path.Combine(repositoryRoot, "src", "Tests")) <> 0
    then failwithf "Unit tests failed"

let integrationTests() =
    if Shell.Exec("dotnet", "run", Path.Combine(repositoryRoot, "src", "LocalTester")) <> 0
    then failwithf "Integration tests failed"

let buildSolution() =
    if Shell.Exec("dotnet", "build -c Release", Path.Combine(repositoryRoot, "src")) <> 0
    then failwithf "Build failed"

[<EntryPoint>]
let main(args: string[]) : int =
    match args with
    | [| "sync-proto-files" |] -> syncProtoFiles()
    | [| "tests" |] ->
        runTests()
    | [| "integration-tests" |] ->
        integrationTests()
    | [| "build" |] ->
        buildSolution()
    | [| "write-schema-subset"  |] ->
        match parseSchemaFromPulumi "azure-native" with
        | Error errorMessage -> printfn $"couldn't parse azure-native schema: {errorMessage}"
        | Ok azureNativeSchema ->
            let resourcesWhichRequireResourceGroupName =
                azureNativeSchema.resources
                |> Map.toList
                |> List.filter (fun (token, resource) ->
                    match Map.tryFind "resourceGroupName" resource.inputProperties with
                    | Some property -> property.required
                    | _ -> false)
               |> List.map fst

            let mainParameterToQueryExistingResources =
                azureNativeSchema.resources
                |> Map.toList
                |> List.map fst
                |> List.collect (fun token ->
                    match token.Split ":" with
                    | [| ns; moduleName; resourceName |] ->
                        let getterInvokeToken = $"{ns}:{moduleName}:get{resourceName}"
                        match Map.tryFind getterInvokeToken azureNativeSchema.functions with
                        | Some functionSchema ->
                            match functionSchema.inputs with
                            | None -> [ ]
                            | Some inputs ->
                                inputs.properties
                                |> Map.toList
                                |> List.filter (fun (inputProperty, property) ->
                                    let propertyName = inputProperty.ToLower()
                                    propertyName <> "resourcegroupname" && propertyName.EndsWith "name" && property.required)
                                |> List.tryHead
                                |> function
                                    | None -> []
                                    | Some (propertyName, _) -> [ getterInvokeToken, propertyName ]
                        | None -> []
                    | _ -> [])
                |> List.append [ "azure-native:resources:getResourceGroup", "resourceGroupName" ]
               
            printfn $"There are {resourcesWhichRequireResourceGroupName.Length} resources which require resourceGroupName"
            let targetFile = Path.Combine(repositoryRoot, "src", "Converter", "Schema.fs")
            let content = StringBuilder()
            let append (input: string) = content.Append input |> ignore
            append "module Converter.Schema\n\n"
            append "let resourcesWhichRequireResourceGroupName = [|\n"
            for resourceToken in resourcesWhichRequireResourceGroupName do
                append $"   \"{resourceToken}\"\n"
            append "|]"
            append "\n\n"
            append "let nameParameterForExistingResources = Map.ofArray [|\n"
            for invokeToken, parameterName in mainParameterToQueryExistingResources do
                append $"   \"{invokeToken}\", \"{parameterName}\"\n"
            append "|]"
            append "\n\n"
            
            File.WriteAllText(targetFile, content.ToString())
            printfn $"Written to {targetFile}"
            
    | otherwise -> printfn $"Unknown build arguments provided %A{otherwise}"
    0
