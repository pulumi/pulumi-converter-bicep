param adminPassword string

resource kv 'Microsoft.KeyVault/vaults@2019-09-01' = {
  name: 'kv-contoso'
  properties: {
    tenantId: subscription().tenantId
    sku: {
      name: 'standard'
      family: 'A'
    }
  }
}

resource adminPwd 'Microsoft.KeyVault/vaults/secrets@2019-09-01' = {
  name: 'admin-password'  
  parent: kv 
  properties: {
    value: adminPassword
  }
}
