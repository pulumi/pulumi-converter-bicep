open Converter
open System
open System.IO
open System.Threading.Tasks
open CliWrap
open CliWrap.Buffered
open System.Linq
open Foundatio.Storage

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

let convertTypescript(directory: string, target: string) = task {
    let! pulumi = pulumiCliBinary()
    let! output =
        Cli.Wrap(pulumi).WithArguments($"convert --from pcl --language typescript --out {target}")
           .WithWorkingDirectory(directory)
           .WithValidation(CommandResultValidation.None)
           .ExecuteBufferedAsync()
    
    if output.ExitCode = 0 then
        return Ok output.StandardOutput
    else
        return Error output.StandardError
}

let rec findParent (directory: string) (fileToFind: string) =
    let path = if Directory.Exists(directory) then directory else Directory.GetParent(directory).FullName
    let files = Directory.GetFiles(path)
    if files.Any(fun file -> Path.GetFileName(file).ToLower() = fileToFind.ToLower())
    then path
    else findParent (DirectoryInfo(path).Parent.FullName) fileToFind

let repositoryRoot = findParent __SOURCE_DIRECTORY__ "README.md"

[<EntryPoint>]
let main (args: string[]) =
    try
        let integrationTests = Path.Combine(repositoryRoot, "integration_tests")
        let directories = Directory.EnumerateDirectories integrationTests
        for example in directories do
            let name = DirectoryInfo(example).Name
            let bicepFilePath = Path.Combine(example, $"{name}.bicep")
            if not (File.Exists bicepFilePath) then
                printfn $"Couldn't find bicep file at {bicepFilePath}"
            else
                let pulumiTargetDirectory = Path.Combine(example, "pulumi")
                let compilationResult = Compile.compileProgramWithComponents {
                    entryBicepSourceFile = bicepFilePath
                    targetDirectory = pulumiTargetDirectory
                    storage = new FolderFileStorage(FolderFileStorageOptions(Folder=example))
                }

                match compilationResult with
                | Error error ->
                    printfn $"Failed to compile bicep file at {bicepFilePath}: {error}"
                | Ok () ->
                    printfn $"Converted bicep into Pulumi at {pulumiTargetDirectory}"
                    let conversion =
                        convertTypescript (pulumiTargetDirectory, Path.Combine(example,"typescript"))
                        |> Async.AwaitTask
                        |> Async.RunSynchronously

                    match conversion with
                    | Error errorMessage ->
                        failwithf $"Failed to convert Pulumi program to TypeScript: {errorMessage}"

                    | Ok _ ->
                        printfn $"Successfully converted {bicepFilePath} to TypeScript"
        0
    with
    | ex ->
        printfn $"Error occured: {ex.Message}"
        1