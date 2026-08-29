param functionAppName string
param newSettings object

resource functionApp 'Microsoft.Web/sites@2023-12-01' existing = {
  name: functionAppName
}

var currentSettings = list('${functionApp.id}/config/appsettings', '2023-12-01').properties
resource appSettings 'Microsoft.Web/sites/config@2023-12-01' = {
  name: 'appsettings'
  parent: functionApp
  properties: union(currentSettings, newSettings) // Combines both, overriding duplicates with newSettings
}
