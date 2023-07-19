import * as pulumi from "@pulumi/pulumi";
import * as azure_native from "@pulumi/azure-native";

const config = new pulumi.Config();
// The name of the resource group to operate on
const resourceGroupName = config.require("resourceGroupName");
const currentResourceGroup = azure_native.resources.getResourceGroupOutput({
    resourceGroupName: resourceGroupName,
});
const storagePrefix = config.require("storagePrefix");
const storageSKU = config.get("storageSKU") || "Standard_LRS";
const location = config.get("location") || currentResourceGroup.apply(currentResourceGroup => currentResourceGroup.location);
const storage = new azure_native.storage.StorageAccount("storage", {
    kind: "StorageV2",
    location: location,
    resourceGroupName: currentResourceGroup.apply(currentResourceGroup => currentResourceGroup.name),
    sku: {
        name: storageSKU,
    },
});
export const storageEndpoint = storage.primaryEndpoints;
