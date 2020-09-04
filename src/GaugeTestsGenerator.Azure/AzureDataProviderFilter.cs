using GaugeTestsGenerator.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace GaugeTestsGenerator.Azure
{
    public class AzureDataProviderFilter : IDataProviderFilter
    {
        public IEnumerable<int> TestSuiteIds { get; set; }
        public string TestPlanId { get; set; }
        public string TestCaseId { get; set; }
    }
}
