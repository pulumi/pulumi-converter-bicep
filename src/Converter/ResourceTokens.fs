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

let fromAzureSpecToPulumi(token: string) =
    if String.IsNullOrWhiteSpace token then
        "azure-native:unknown:unknown"
    else
        match token.Split "@" with
        | [| fullQualifiedTypeName; version |] ->
            let moduleName, resource = moduleAndResource fullQualifiedTypeName
            let version = version.Replace("-", "")
            $"azure-native:{moduleName}/v{version}:{resource}"

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
        $"azure-native:{moduleName}:get{resource}"

    | [| fullQualifiedTypeName |] ->
        // no version specified
        let moduleName, resource = moduleAndResource fullQualifiedTypeName
        $"azure-native:{moduleName}:get{resource}"

    | _ ->
        "azure-native:unknown:unknown"