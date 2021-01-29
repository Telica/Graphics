﻿using UnityEngine.UIElements;

namespace UnityEditor.ShaderGraph.Drawing
{
    interface ISGViewModel
    {
        VisualElement ParentView { get; set; }

        // Wipes all data in this view-model
        void Reset();
    }
}
