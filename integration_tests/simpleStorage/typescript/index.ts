import * as pulumi from "@pulumi/pulumi";
import * as azure_native from "@pulumi/azure-native";

const config = new pulumi.Config();
// The name of the resource group to operate on
const resourceGroupName = config.require("resourceGroupName");
const currentResourceGroup = azure_native.resources.getResourceGroupOutput({
    resourceGroupName: resourceGroupName,
});
const storage = new azure_native.storage.StorageAccount("storageaccount", {
    kind: "StorageV2",
    location: currentResourceGroup.apply(currentResourceGroup => currentResourceGroup.location),
    resourceGroupName: currentResourceGroup.apply(currentResourceGroup => currentResourceGroup.name),
    sku: {
        name: "Standard_LRS",
    },
});
