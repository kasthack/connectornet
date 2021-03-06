﻿// Copyright © 2013, Oracle and/or its affiliates. All rights reserved.
//
// MySQL Connector/NET is licensed under the terms of the GPLv2
// <http://www.gnu.org/licenses/old-licenses/gpl-2.0.html>, like most 
// MySQL Connectors. There are special exceptions to the terms and 
// conditions of the GPLv2 as it is applied to this software, see the 
// FLOSS License Exception
// <http://www.mysql.com/about/legal/licensing/foss-exception.html>.
//
// This program is free software; you can redistribute it and/or modify 
// it under the terms of the GNU General Public License as published 
// by the Free Software Foundation; version 2 of the License.
//
// This program is distributed in the hope that it will be useful, but 
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY 
// or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License 
// for more details.
//
// You should have received a copy of the GNU General Public License along 
// with this program; if not, write to the Free Software Foundation, Inc., 
// 51 Franklin St, Fifth Floor, Boston, MA 02110-1301  USA

using System.Collections.Generic;
using System.Configuration;
using GCEC = MySql.Data.MySqlClient.GenericConfigurationElementCollection<MySql.Data.MySqlClient.InterceptorConfigurationElement>;
using GCECA = MySql.Data.MySqlClient.GenericConfigurationElementCollection<MySql.Data.MySqlClient.AuthenticationPluginConfigurationElement>;
namespace MySql.Data.MySqlClient {
    public sealed class MySqlConfiguration : ConfigurationSection {
        public static MySqlConfiguration Settings { get; } = ConfigurationManager.GetSection( "MySQL" ) as MySqlConfiguration;

        [ConfigurationProperty( "ExceptionInterceptors", IsRequired = false )]
        [ConfigurationCollection( typeof(InterceptorConfigurationElement), AddItemName = "add", ClearItemsName = "clear", RemoveItemName = "remove" )]
        public GCEC ExceptionInterceptors => (GCEC) this[ "ExceptionInterceptors" ];

        [ConfigurationProperty( "CommandInterceptors", IsRequired = false )]
        [ConfigurationCollection( typeof(InterceptorConfigurationElement), AddItemName = "add", ClearItemsName = "clear", RemoveItemName = "remove" )]
        public GCEC CommandInterceptors
            => ( GCEC ) this[ "CommandInterceptors" ];

        [ConfigurationProperty( "AuthenticationPlugins", IsRequired = false )]
        [ConfigurationCollection( typeof( AuthenticationPluginConfigurationElement ), AddItemName = "add", ClearItemsName = "clear", RemoveItemName = "remove" )]
        public GCECA AuthenticationPlugins
            => (GCECA) this[ "AuthenticationPlugins" ];

        [ConfigurationProperty( "Replication", IsRequired = true )]
        public ReplicationConfigurationElement Replication {
            get {
                return (ReplicationConfigurationElement) this[ "Replication" ];
            }
            set {
                this[ "Replication" ] = value;
            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public sealed class AuthenticationPluginConfigurationElement : ConfigurationElement {
        [ConfigurationProperty( "name", IsRequired = true )]
        public string Name {
            get {
                return (string) this[ "name" ];
            }
            set {
                this[ "name" ] = value;
            }
        }

        [ConfigurationProperty( "type", IsRequired = true )]
        public string Type {
            get {
                return (string) this[ "type" ];
            }
            set {
                this[ "type" ] = value;
            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public sealed class InterceptorConfigurationElement : ConfigurationElement {
        [ConfigurationProperty( "name", IsRequired = true )]
        public string Name {
            get {
                return (string) this[ "name" ];
            }
            set {
                this[ "name" ] = value;
            }
        }

        [ConfigurationProperty( "type", IsRequired = true )]
        public string Type {
            get {
                return (string) this[ "type" ];
            }
            set {
                this[ "type" ] = value;
            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class GenericConfigurationElementCollection<T> : ConfigurationElementCollection, IEnumerable<T>
        where T : ConfigurationElement, new() {
        private readonly List<T> _elements = new List<T>();

        protected override ConfigurationElement CreateNewElement() {
            var newElement = new T();
            _elements.Add( newElement );
            return newElement;
        }

        protected override object GetElementKey( ConfigurationElement element ) => _elements.Find( e => e.Equals( element ) );

        public new IEnumerator<T> GetEnumerator() => _elements.GetEnumerator();
    }
}