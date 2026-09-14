using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace EWFM.AwacsMes
{
    /// <summary>
    /// Represents an Awacs workorder attribute.
    /// </summary>
    public class Attribute : IEquatable<Attribute>
    {
        /// <summary>
        /// Gets or sets the attribute name.
        /// </summary>
        [XmlElement(ElementName = "Attribute")]
        public string Name;

        /// <summary>
        /// Gets or sets the attribute value.
        /// </summary>
        public string Value;

        /// <summary>
        /// Initializes a new instance of Attribute.
        /// </summary>
        public Attribute()
        {
        }

        /// <summary>
        /// Determines whether this instance of Attribute is equal to the specified Attribute object.
        /// </summary>
        /// <param name="other">The Attribute object to compare with.</param>
        /// <returns>true if this instance is equal to the specified Attribute object; otherwise, false.</returns>
        public bool Equals(Attribute other)
        {
            return (this.Name == other.Name);
        }

        /// <summary>
        /// Initializes a new instance of Attribute with the specified name and value.
        /// </summary>
        /// <param name="attribute">The name of the attribute.</param>
        /// <param name="value">The value of the attribute.</param>
        public Attribute(string attribute, string value)
        {
            this.Name = attribute;
            this.Value = value;
        }
    }
}
