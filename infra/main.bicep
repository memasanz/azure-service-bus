// Deploys an Azure Service Bus namespace (Standard tier) with the entities
// used by the notebook walkthrough: queues, a session-enabled queue,
// a topic, and three subscriptions (default, SQL-filtered, correlation-filtered).
//
// Optional: pass `principalId` (object id of a user, group, or service principal)
// to grant 'Azure Service Bus Data Owner' for use with DefaultAzureCredential
// in notebook 10.

@description('Service Bus namespace name. Must be globally unique, 6-50 chars, alphanumeric and hyphens.')
@minLength(6)
@maxLength(50)
param namespaceName string

@description('Azure region for the deployment.')
param location string = resourceGroup().location

@description('SKU tier for the Service Bus namespace.')
@allowed([
  'Standard'
  'Premium'
])
param skuName string = 'Standard'

@description('Optional principalId (objectId) to assign Azure Service Bus Data Owner. Leave empty to skip.')
param principalId string = ''

@description('Tags applied to all resources.')
param tags object = {
  project: 'servicebus-demo'
}

// --------------------------------------------------------------------------------------
// Namespace
// --------------------------------------------------------------------------------------
resource sbNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: namespaceName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuName
  }
  properties: {
    disableLocalAuth: false
    zoneRedundant: false
  }
}

// --------------------------------------------------------------------------------------
// Queues
// --------------------------------------------------------------------------------------
resource demoQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: sbNamespace
  name: 'demo-queue'
  properties: {
    lockDuration: 'PT30S'
    maxDeliveryCount: 10
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
    enablePartitioning: false
  }
}

resource sessionQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: sbNamespace
  name: 'demo-sessions'
  properties: {
    requiresSession: true
    lockDuration: 'PT30S'
    maxDeliveryCount: 10
    defaultMessageTimeToLive: 'P14D'
  }
}

resource dlqDemoQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: sbNamespace
  name: 'demo-dlq'
  properties: {
    lockDuration: 'PT30S'
    maxDeliveryCount: 3
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
  }
}

// --------------------------------------------------------------------------------------
// Topic + Subscriptions (with filters)
// --------------------------------------------------------------------------------------
resource demoTopic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: sbNamespace
  name: 'demo-topic'
  properties: {
    defaultMessageTimeToLive: 'P14D'
    enablePartitioning: false
  }
}

resource subAll 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: demoTopic
  name: 'all'
  properties: {
    lockDuration: 'PT30S'
    maxDeliveryCount: 10
  }
}

resource subHighPriority 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: demoTopic
  name: 'high-priority'
  properties: {
    lockDuration: 'PT30S'
    maxDeliveryCount: 10
  }
}

resource subHighPriorityRule 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2022-10-01-preview' = {
  parent: subHighPriority
  name: 'high-priority-rule'
  properties: {
    filterType: 'SqlFilter'
    sqlFilter: {
      sqlExpression: 'priority = \'high\''
    }
  }
}

resource subOrders 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: demoTopic
  name: 'orders'
  properties: {
    lockDuration: 'PT30S'
    maxDeliveryCount: 10
  }
}

resource subOrdersRule 'Microsoft.ServiceBus/namespaces/topics/subscriptions/rules@2022-10-01-preview' = {
  parent: subOrders
  name: 'orders-correlation'
  properties: {
    filterType: 'CorrelationFilter'
    correlationFilter: {
      label: 'order'
    }
  }
}

// --------------------------------------------------------------------------------------
// Optional RBAC for DefaultAzureCredential (notebook 10)
// 'Azure Service Bus Data Owner' = 090c5cfd-751d-490a-894a-3ce6f1109419
// --------------------------------------------------------------------------------------
var dataOwnerRoleId = '090c5cfd-751d-490a-894a-3ce6f1109419'

resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(principalId)) {
  name: guid(sbNamespace.id, principalId, dataOwnerRoleId)
  scope: sbNamespace
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', dataOwnerRoleId)
    principalId: principalId
    principalType: 'User'
  }
}

// --------------------------------------------------------------------------------------
// Outputs
// --------------------------------------------------------------------------------------
output namespaceName string = sbNamespace.name
output namespaceHostname string = '${sbNamespace.name}.servicebus.windows.net'
output queueName string = demoQueue.name
output sessionQueueName string = sessionQueue.name
output topicName string = demoTopic.name

#disable-next-line outputs-should-not-contain-secrets
output primaryConnectionString string = listKeys('${sbNamespace.id}/AuthorizationRules/RootManageSharedAccessKey', '2022-10-01-preview').primaryConnectionString
