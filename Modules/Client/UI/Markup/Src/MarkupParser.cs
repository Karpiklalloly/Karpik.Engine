using System;
using System.Xml;
using System.Collections.Generic;
using Karpik.Engine.Client.UI;

namespace Karpik.Engine.Client.UI.Markup
{
    /// <summary>
    /// Simple XML-based markup parser for UI.
    /// </summary>
    public static class MarkupParser
    {
        public static UiMarkupResource LoadFromXml(string xml)
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var root = doc.DocumentElement;
            if (root == null || root.Name != "UI")
                throw new InvalidOperationException("Root element must be <UI>");

            var nodes = new List<UiNode>();
            var propsBlob = new List<byte>();
            var stringTable = new List<string>();
            var bindings = new List<Binding>();
            var handlers = new List<ClickHandler>();

            // We'll implement a simple recursive parser later.
            // For now, return empty resource.
            return new UiMarkupResource(nodes.ToArray(), propsBlob.ToArray(), stringTable.ToArray(), bindings.ToArray(), handlers.ToArray());
        }
    }

    public class UiMarkupResource
    {
        public UiNode[] Nodes { get; }
        public byte[] PropsBlob { get; }
        public string[] StringTable { get; }
        public Binding[] Bindings { get; }
        public ClickHandler[] Handlers { get; }

        public UiMarkupResource(UiNode[] nodes, byte[] propsBlob, string[] stringTable, Binding[] bindings, ClickHandler[] handlers)
        {
            Nodes = nodes;
            PropsBlob = propsBlob;
            StringTable = stringTable;
            Bindings = bindings;
            Handlers = handlers;
        }
    }
}