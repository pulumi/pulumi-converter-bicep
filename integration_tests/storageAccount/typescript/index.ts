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
const storageLocations = [
    "east",
    "west",
];
const storage = new azure_native.storage.StorageAccount("storage", {
    kind: "StorageV2",
    location: location,
    resourceGroupName: currentResourceGroup.apply(currentResourceGroup => currentResourceGroup.name),
    sku: {
        name: storageSKU,
    },
});
const storageAccounts: azure_native.storage.StorageAccount[] = [];
for (const range = {value: 0}; range.value < 10; range.value++) {
    storageAccounts.push(new azure_native.storage.StorageAccount(`storageAccounts-${range.value}`, {
        kind: "StorageV2",
        location: location,
        resourceGroupName: currentResourceGroup.apply(currentResourceGroup => currentResourceGroup.name),
        sku: {
            name: storageSKU,
        },
    }));
}
const storageAccountsByLocation: azure_native.storage.StorageAccount[] = [];
for (const range of storageLocations.map((v, k) => ({key: k, value: v}))) {
    storageAccountsByLocation.push(new azure_native.storage.StorageAccount(`storageAccountsByLocation-${range.key}`, {
        kind: "StorageV2",
        location: range.value,
        resourceGroupName: currentResourceGroup.apply(currentResourceGroup => currentResourceGroup.name),
        sku: {
            name: storageSKU,
        },
    }));
}
const exampleExistingStorage = azure_native.storage.getStorageAccountOutput({
    accountName: "existingStorageName",
    resourceGroupName: currentResourceGroup.apply(currentResourceGroup => currentResourceGroup.name),
});
const myExistingResourceGroup = azure_native.resources.getResourceGroupOutput({
    resourceGroupName: "existingResourceGroupName",
});
export const storageEndpoint = storage.primaryEndpoints;
export const existingEndpoint = exampleExistingStorage.apply(exampleExistingStorage => exampleExistingStorage.primaryEndpoints);
