using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GeradorCodigoGauge.Console
{
    public class SharedStepConfig
    {
        public string RegexDe { get; set; }
        public string De { get; set; }
        public string Para { get; set; }
        public bool Criado { get; set; }
        public string RegexParam { get; set; }
        public Dictionary<string, string> ParamDePara { get; set; }
    }
}
