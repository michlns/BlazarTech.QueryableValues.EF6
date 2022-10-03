﻿namespace BlazarTech.QueryableValues
{
    /// <summary>
    /// Serialization options.
    /// </summary>
    public enum SerializationOptions
    {
        /// <summary>
        /// Use the JSON serializer if possible, otherwise it will use XML.
        /// This is the default.
        /// </summary>
        /// <remarks>
        /// This option causes an additional roundtrip to the database to check if JSON serialization is supported. This only happens once per connection string uniqueness.
        /// </remarks>
        Auto,

        /// <summary>
        /// Use the JSON serializer.
        /// </summary>
        /// <remarks>
        /// <para>
        /// In my tests, JSON significantly outperforms XML, particularly on big sequences.
        /// <br/>
        /// <br/>
        /// JSON can only be used when the following is true:<br/>
        /// - The SQL Server instance is 2016 and above.<br/>
        /// - The database has a compatibility level of 130 or higher.
        /// <br/>
        /// <br/>
        /// More info: <see href="https://learn.microsoft.com/en-us/sql/t-sql/functions/openjson-transact-sql"/>
        /// </para>
        /// <br/>
        /// <b>WARNING:</b> An error will occur at runtime if JSON serialization is not supported.
        /// </remarks>
        UseJson,

        /// <summary>
        /// Use the XML serializer.
        /// </summary>
        UseXml
    }
}
