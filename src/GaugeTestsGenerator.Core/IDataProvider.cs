using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace GaugeTestsGenerator.Core
{
    public interface IDataProvider
    {
        Task LoadTests(IDataProviderFilter filter);
    }
}
