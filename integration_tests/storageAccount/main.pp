config resourceGroupName "string" {
    description = "The name of the resource group to operate on"
}
currentResourceGroup = invoke("azure-native:resources:getResourceGroup", {
"resourceGroupName" = resourceGroupName
})
config storagePrefix "string" {
}
config storageSKU "string" {
    default = "Standard_LRS"
}
config location "string" {
    default = currentResourceGroup.location
}
resource storage "azure-native:storage:StorageAccount" {
    kind = "StorageV2"
    location = location
    resourceGroupName = currentResourceGroup.name
    sku = {
    name = storageSKU
    }
}
output storageEndpoint {
    value = storage.primaryEndpoints
}
