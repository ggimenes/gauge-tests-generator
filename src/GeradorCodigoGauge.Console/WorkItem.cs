using System;
using System.Collections.Generic;
using System.Data;
using System.Xml.Serialization;

namespace GeradorCodigoGauge.Console
{
    public class WorkItem
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Title { get; set; }
        public string StepsString { get; set; }
        public Steps Steps { get; set; }
        public string ParametersString { get; set; }
        public Parameters Parameters { get; set; }
        /// <summary>
        /// Exclusivo do shared parameters
        /// </summary>
        public ParameterSet ParameterSet { get; set; }
        public string LocalDataSourceString { get; set; }
        /// <summary>
        /// Campo LocalDataSource em formato Json, trazendo o mapeamento dos shared parameters
        /// </summary>
        public LocalDataSourceJson SharedParametersMap { get; set; }

        /// <summary>
        /// Campo LocalDataSource em formato XML, trazendo o valor dos parâmetros, quando o parâmetro não é shared
        /// </summary>
        public DataTable ParametersValue { get; set; }
    }

    #region SharedParameters

    [XmlRoot(ElementName = "paramNames")]
    public class ParamNames
    {
        [XmlElement(ElementName = "param")]
        public List<string> Param { get; set; }
    }

    [XmlRoot(ElementName = "kvp")]
    public class Kvp
    {
        [XmlAttribute(AttributeName = "key")]
        public string Key { get; set; }
        [XmlAttribute(AttributeName = "value")]
        public string Value { get; set; }
    }

    [XmlRoot(ElementName = "dataRow")]
    public class DataRow
    {
        [XmlElement(ElementName = "kvp")]
        public List<Kvp> Kvp { get; set; }
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; }
    }

    [XmlRoot(ElementName = "paramData")]
    public class ParamData
    {
        [XmlElement(ElementName = "dataRow")]
        public List<DataRow> DataRow { get; set; }
        [XmlAttribute(AttributeName = "lastId")]
        public string LastId { get; set; }
    }

    [XmlRoot(ElementName = "parameterSet")]
    public class ParameterSet
    {
        [XmlElement(ElementName = "paramNames")]
        public ParamNames ParamNames { get; set; }
        [XmlElement(ElementName = "paramData")]
        public ParamData ParamData { get; set; }
    }
    #endregion

    [XmlRoot(ElementName = "param")]
    public class Param
    {
        [XmlAttribute(AttributeName = "name")]
        public string Name { get; set; }
        [XmlAttribute(AttributeName = "bind")]
        public string Bind { get; set; }
    }

    [XmlRoot(ElementName = "parameters")]
    public class Parameters
    {
        [XmlElement(ElementName = "param")]
        public List<Param> Param { get; set; }
    }

    public class LocalDataSourceJson
    {
        public Parametermap[] parameterMap { get; set; }
        public int[] sharedParameterDataSetIds { get; set; }
        public int rowMappingType { get; set; }
    }

    public class Parametermap
    {
        public string localParamName { get; set; }
        public string sharedParameterName { get; set; }
        public int sharedParameterDataSetId { get; set; }
    }



    [XmlRoot(ElementName = "parameterizedString")]
    public class ParameterizedString
    {
        [XmlAttribute(AttributeName = "isformatted")]
        public string Isformatted { get; set; }
        [XmlText]
        public string Text { get; set; }
    }

    [XmlRoot(ElementName = "step")]
    public class Step
    {
        [XmlElement(ElementName = "parameterizedString")]
        public List<ParameterizedString> ParameterizedString { get; set; }

        [XmlElement(ElementName = "description")]
        public string Description { get; set; }

        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; }

        [XmlAttribute(AttributeName = "type")]
        public string Type { get; set; }
    }

    [XmlRoot(ElementName = "compref")]
    public class Compref
    {
        [XmlElement(ElementName = "step")]
        public List<Step> Step { get; set; }

        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; }

        [XmlAttribute(AttributeName = "ref")]
        public string Ref { get; set; }

        [XmlElement(ElementName = "compref")]
        public Compref compref { get; set; }
    }

    [XmlRoot(ElementName = "steps")]
    public class Steps
    {
        [XmlElement(ElementName = "compref")]
        public Compref Compref { get; set; }

        [XmlElement(ElementName = "step")]
        public List<Step> Step { get; set; }

        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; }

        [XmlAttribute(AttributeName = "last")]
        public string Last { get; set; }
    }
}
