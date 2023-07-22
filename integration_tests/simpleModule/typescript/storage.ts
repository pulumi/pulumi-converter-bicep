import * as pulumi from "@pulumi/pulumi";
import * as azure_native from "@pulumi/azure-native";

interface StorageArgs {
    /**
     * The name of the resource group to operate on
     */
    resourceGroupName: pulumi.Input<string>,
    location: pulumi.Input<string>,
}

export class Storage extends pulumi.ComponentResource {
    constructor(name: string, args: StorageArgs, opts?: pulumi.ComponentResourceOptions) {
        super("components:index:Storage", name, args, opts);
        const storage = new azure_native.storage.StorageAccount(`${name}-storage`, {
            kind: "StorageV2",
            location: args.location,
            resourceGroupName: args.resourceGroupName,
            sku: {
                name: "Standard_LRS",
            },
        }, {
            parent: this,
        });

        const exampleExistingStorage = azure_native.storage.getStorageAccountOutput({
            accountName: "existingStorageName",
            resourceGroupName: args.resourceGroupName,
        });

        this.registerOutputs();
    }
}
