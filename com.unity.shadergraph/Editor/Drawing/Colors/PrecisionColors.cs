using System;
using System.Linq;
using UnityEditor.ShaderGraph.Internal;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.ShaderGraph.Drawing.Colors
{
    class PrecisionColors : ColorProviderFromStyleSheet
    {
        public override string GetTitle() => "Precision";

        public override bool AllowCustom() => false;

        public override bool ClearOnDirty() => true;

        protected override bool GetClassFromNode(AbstractMaterialNode node, out string ussClass)
        {
            var graphPrecision = node.graphPrecision;
            if (graphPrecision == GraphPrecision.Graph)
            {
                if (node.owner.isSubGraph)
                {
                    // display based on subgraph's "switchable" graph precision
                    ussClass = node.owner.graphPrecision.ToString();
                }
                else
                {
                    // display on shadergraph's concrete precision
                    ussClass = node.owner.concretePrecision.ToString();
                }
            }
            else
            {
                // node chose something -- use that
                ussClass = graphPrecision.ToString();
            }

            return !string.IsNullOrEmpty(ussClass);
        }

        public override void ClearColor(IShaderNodeView nodeView)
        {
            foreach (var type in GraphPrecision.GetValues(typeof(GraphPrecision)))
            {
                nodeView.colorElement.RemoveFromClassList(type.ToString());
            }
        }
    }

    class GraphPrecisionColors : ColorProviderFromStyleSheet
    {
        public override string GetTitle() => "Graph Precision";

        public override bool AllowCustom() => false;

        public override bool ClearOnDirty() => true;

        protected override bool GetClassFromNode(AbstractMaterialNode node, out string ussClass)
        {
            ussClass = node.graphPrecision.ToString();

            return !string.IsNullOrEmpty(ussClass);
        }

        public override void ClearColor(IShaderNodeView nodeView)
        {
            foreach (var type in GraphPrecision.GetValues(typeof(GraphPrecision)))
            {
                nodeView.colorElement.RemoveFromClassList(type.ToString());
            }
        }
    }
}
