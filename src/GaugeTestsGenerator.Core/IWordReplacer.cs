using System;
using System.Collections.Generic;
using System.Text;

namespace GaugeTestsGenerator.Core
{
    public interface IWordReplacer
    {
        string FromTo(string word);
    }
}
