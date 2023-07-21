config resourceGroupName "string" {
    description = "The name of the resource group to operate on"
}
currentResourceGroup = invoke("azure-native:resources:getResourceGroup", {
    resourceGroupName = resourceGroupName
})
resource storage "azure-native:storage:StorageAccount" {
    __logicalName = "storageaccount"
    kind = "StorageV2"
    location = currentResourceGroup.location
    resourceGroupName = currentResourceGroup.name
    sku = {
        name = "Standard_LRS"
    }
}
