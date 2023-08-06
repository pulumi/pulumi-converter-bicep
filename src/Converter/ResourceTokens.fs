module Converter.ResourceTokens

open System
open Humanizer

let private moduleAndResource (fullQualifiedTypeName: string) =
    match fullQualifiedTypeName.Split "/" with
    | [|  |] ->
        "unknown", "unknown"
    | segments ->
        let resourceNamespace = segments[0]
        let resourceName = segments[segments.Length - 1]
        let resourceModule =
            if resourceNamespace.StartsWith "Microsoft." then
                resourceNamespace.Substring(10).Replace(".", "").ToLower()
            else
                resourceNamespace.Replace(".", "").ToLower()
                
        let resource = resourceName.Pascalize().Singularize(true)
        resourceModule, resource

[<RequireQualifiedAccess>]
type AzureVersion =
    | Stable of DateOnly
    | Preview of DateOnly

let serializeDate (date: DateOnly) =
    let year = date.Year.ToString()
    let month = date.Month.ToString().PadLeft(2, '0')
    let day = date.Day.ToString().PadLeft(2, '0')
    $"{year}{month}{day}"

let serializeVersion = function
    | AzureVersion.Stable date -> $"v{serializeDate date}"
    | AzureVersion.Preview date -> $"v{serializeDate date}preview"

let (|Int|_|) (input: string) =
    match Int32.TryParse input with
    | true, x -> Some x
    | _ -> None
    
let parseBicepVersion (version: string) =
    match version.Split "-" with
    | [| Int year; Int month; Int day; "preview" |] ->
        let version = AzureVersion.Preview (DateOnly(year, month, day))
        Some version
    | [| Int year; Int month; Int day |] ->
        let version = AzureVersion.Stable (DateOnly(year, month, day))
        Some version
    | _ ->
        None

let parsePulumiVersion (version: string) =
    let isPreview = version.EndsWith "preview"
    let version = version.Replace("preview", "").TrimStart 'v'
    let year = version.Substring(0, 4)
    let month = version.Substring(4, 2)
    let day = version.Substring(6, 2)
    match year, month, day with
    | Int year, Int month, Int day ->
        let azureVersion =
            if isPreview
            then AzureVersion.Preview(DateOnly(year, month, day))
            else AzureVersion.Stable(DateOnly(year, month, day))
            
        Some azureVersion

    | _ ->
        None

let chooseVersion (input: AzureVersion) (availableVersions: AzureVersion list) =
    if List.contains input availableVersions then
        // an exact version is available, choose it
        Some input
    else
        availableVersions
        |> List.sortBy (function
           | AzureVersion.Stable stableDate -> stableDate
           | AzureVersion.Preview previewDate -> previewDate.AddDays -1)
        |> List.tryFind (fun availableVersion ->
            let availableVersionDate =
                match availableVersion with
                | AzureVersion.Stable stableDate -> stableDate
                | AzureVersion.Preview previewDate -> previewDate
            let inputExactVersion =
                match input with
                | AzureVersion.Stable stableDate -> stableDate
                | AzureVersion.Preview previewDate -> previewDate
            // find the first available version which is greater than the input version
            availableVersionDate > inputExactVersion)

let computeAvailableVersion (moduleName: string) (bicepVersion: string) =
    match Map.tryFind moduleName Schema.availableModuleVersions with
    | None ->
        // No available versions, use the default
        None
    | Some [singleAvailableVersion] ->
        // Only one version available, use the default
        None
    | Some availableVersions ->
        // multiple versions available
        // choose exact version if available or next stable version
        match parseBicepVersion bicepVersion with
        | None ->
            // couldn't parse the version, use the default
            None
        | Some version ->
            let versions = List.choose id [for version in availableVersions -> parsePulumiVersion version]
            chooseVersion version versions

let fromAzureSpecToPulumi(token: string) =
    if String.IsNullOrWhiteSpace token then
        "azure-native:unknown:unknown"
    else
        match token.Split "@" with
        | [| fullQualifiedTypeName; version |] ->
            let moduleName, resource = moduleAndResource fullQualifiedTypeName
            match computeAvailableVersion moduleName version with
            | None ->
                // use default version
                $"azure-native:{moduleName}:{resource}"
            | Some foundVersion ->
                $"azure-native:{moduleName}/{serializeVersion foundVersion}:{resource}"

        | [| fullQualifiedTypeName |] ->
            // no version specified
            let moduleName, resource = moduleAndResource fullQualifiedTypeName
            $"azure-native:{moduleName}:{resource}"

        | _ ->
            "azure-native:unknown:unknown"

let fromAzureSpecToPulumiWithoutVersion(token: string) =
    if String.IsNullOrWhiteSpace token then
        "azure-native:unknown:unknown"
    else
        match token.Split "@" with
        | [| fullQualifiedTypeName; version |] -> fromAzureSpecToPulumi fullQualifiedTypeName
        | [| fullQualifiedTypeName |] -> fullQualifiedTypeName
        | _ -> "azure-native:unknown:unknown"

let fromAzureSpecToExistingResourceToken (token: string) =
    match token.Split "@" with
    | [| fullQualifiedTypeName; version |] ->
        let moduleName, resource = moduleAndResource fullQualifiedTypeName
        match computeAvailableVersion moduleName version with
        | None -> 
            $"azure-native:{moduleName}:get{resource}"
        | Some foundVersion ->
            $"azure-native:{moduleName}/{serializeVersion foundVersion}:get{resource}"

    | [| fullQualifiedTypeName |] ->
        // no version specified
        let moduleName, resource = moduleAndResource fullQualifiedTypeName
        $"azure-native:{moduleName}:get{resource}"

    | _ ->
        "azure-native:unknown:unknown"