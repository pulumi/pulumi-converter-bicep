import * as pulumi from "@pulumi/pulumi";
import * as azure_native from "@pulumi/azure-native";

const config = new pulumi.Config();
// The name of the resource group to operate on
const resourceGroupName = config.require("resourceGroupName");
const currentResourceGroup = azure_native.resources.getResourceGroupOutput({
    resourceGroupName: resourceGroupName,
});
const currentClientConfig = azure_native.authorization.getClientConfig({});
const adminPassword = config.require("adminPassword");
const kv = new azure_native.keyvault.Vault("kv-contoso", {
    properties: {
        sku: {
            family: "A",
            name: azure_native.keyvault.SkuName.Standard,
        },
        tenantId: currentClientConfig.then(currentClientConfig => currentClientConfig.tenantId),
    },
    resourceGroupName: currentResourceGroup.apply(currentResourceGroup => currentResourceGroup.name),
    vaultName: "kv-contoso",
});
const adminPwd = new azure_native.keyvault.Secret("admin-password", {
    properties: {
        value: adminPassword,
    },
    resourceGroupName: currentResourceGroup.apply(currentResourceGroup => currentResourceGroup.name),
    secretName: "admin-password",
}, {
    parent: kv,
});
