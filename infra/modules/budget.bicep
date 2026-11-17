// Monthly cost budget of the resource group with notifications at 50 %, 80 % and 100 % of the amount.
// The notifications go to the subscription Owners, so no email address is stored in the repository.

param environmentName string

@minValue(1)
param amount int

@description('First day of the month the budget starts in, as yyyy-MM-01.')
param startDate string

var thresholds = [50, 80, 100]

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: 'budget-bajkowecuda-${environmentName}'
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
    }
    notifications: toObject(
      thresholds,
      threshold => 'actual-above-${threshold}-percent',
      threshold => {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: threshold
        thresholdType: 'Actual'
        contactEmails: []
        contactRoles: [
          'Owner'
        ]
      }
    )
  }
}
