﻿using System;

namespace Toastify.Common
{
    [AttributeUsage(AttributeTargets.Field)]
    public class ComboBoxItemAttribute : Attribute
    {
        public static readonly ComboBoxItemAttribute Default = new ComboBoxItemAttribute();

        public string Content { get; }

        public string Tooltip { get; }

        public ComboBoxItemAttribute() : this(null)
        {
        }

        public ComboBoxItemAttribute(string content) : this(content, null)
        {
        }

        public ComboBoxItemAttribute(string content, string tooltip)
        {
            this.Content = content;
            this.Tooltip = tooltip;
        }
    }
}