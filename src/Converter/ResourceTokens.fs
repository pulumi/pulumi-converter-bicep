module Converter.ResourceTokens

open System
open Humanizer

let private moduleAndResource (fullQualifiedTypeName: string) =
    match fullQualifiedTypeName.Split "/" with
    | [| resourceNamespace; resourceName |] ->
        let resourceModule =
            if resourceNamespace.StartsWith "Microsoft." then
                resourceNamespace.Substring(10).Replace(".", "").ToLower()
            else
                resourceNamespace.Replace(".", "").ToLower()
                
        let resource = resourceName.Pascalize().Singularize(true)
        resourceModule, resource

    | _ ->
        "unknown", "unknown"

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