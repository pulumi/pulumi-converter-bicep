config resourceGroupName "string" {
    description = "The name of the resource group to operate on"
}
currentResourceGroup = invoke("azure-native:resources:getResourceGroup", {
    resourceGroupName = resourceGroupName
})
component main "./storage" {
    __logicalName = "storageModule"
    location = currentResourceGroup.location
    resourceGroupName = currentResourceGroup.name
}
