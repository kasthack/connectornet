﻿// Copyright © 2013, 2014 Oracle and/or its affiliates. All rights reserved.
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

using System;
using System.Collections.Generic;
using System.Data;

namespace MySql.Data.MySqlClient {
    public class MySqlSchemaCollection {
        private readonly List<SchemaColumn> _columns = new List<SchemaColumn>();
        private readonly List<MySqlSchemaRow> _rows = new List<MySqlSchemaRow>();
        private readonly DataTable _table;
        public MySqlSchemaCollection() {
            Mapping = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
            LogicalMappings = new Dictionary<int, int>();
        }

        public MySqlSchemaCollection( string name ) : this() { Name = name; }
        public MySqlSchemaCollection( DataTable dt ) : this() {
            // cache the original datatable to avoid the overhead of creating again whenever possible.
            _table = dt;
            var i = 0;
            foreach ( DataColumn dc in dt.Columns ) {
                _columns.Add( new SchemaColumn { Name = dc.ColumnName, Type = dc.DataType } );
                Mapping.Add( dc.ColumnName, i++ );
                LogicalMappings[ _columns.Count - 1 ] = _columns.Count - 1;
            }
            //todo:check types
            foreach ( DataRow dr in dt.Rows ) {
                var row = new MySqlSchemaRow( this );
                for ( i = 0; i < _columns.Count; i++ ) row[ i ] = dr[ i ];
                _rows.Add( row );
            }
        }
        internal Dictionary<string, int> Mapping;
        internal Dictionary<int, int> LogicalMappings;
        public string Name { get; set; }
        public IList<SchemaColumn> Columns => _columns;
        public IList<MySqlSchemaRow> Rows => _rows;

        internal SchemaColumn AddColumn( string name, Type t ) {
            var c = new SchemaColumn { Name = name, Type = t };
            _columns.Add( c );
            Mapping.Add( name, _columns.Count - 1 );
            LogicalMappings[ _columns.Count - 1 ] = _columns.Count - 1;
            return c;
        }

        internal int ColumnIndex( string name ) {
            var index = -1;
            for ( var i = 0; i < _columns.Count; i++ ) {
                var c = _columns[ i ];
                if ( String.Compare( c.Name, name, StringComparison.OrdinalIgnoreCase ) != 0 ) continue;
                index = i;
                break;
            }
            return index;
        }

        internal void RemoveColumn( string name ) {
            var index = ColumnIndex( name );
            if ( index == -1 ) throw new InvalidOperationException();
            _columns.RemoveAt( index );
            for ( var i = index; i < Columns.Count; i++ ) LogicalMappings[ i ] = LogicalMappings[ i ] + 1;
        }

        internal bool ContainsColumn( string name ) => ColumnIndex( name ) >= 0;

        internal MySqlSchemaRow AddRow() {
            var r = new MySqlSchemaRow( this );
            _rows.Add( r );
            return r;
        }

        internal MySqlSchemaRow NewRow() => new MySqlSchemaRow( this );

        internal DataTable AsDataTable() {
            if ( _table != null ) return _table;
            var dt = new DataTable( Name );
            foreach ( var col in Columns ) dt.Columns.Add( col.Name, col.Type );
            foreach ( var row in Rows ) {
                var newRow = dt.NewRow();
                for ( var i = 0; i < dt.Columns.Count; i++ ) newRow[ i ] = row[ i ] ?? DBNull.Value;
                dt.Rows.Add( newRow );
            }
            return dt;
        }
    }

    public class MySqlSchemaRow {
        private Dictionary<int, object> _data;

        public MySqlSchemaRow( MySqlSchemaCollection c ) {
            Collection = c;
            InitMetadata();
        }

        internal void InitMetadata() { _data = new Dictionary<int, object>(); }

        internal MySqlSchemaCollection Collection { get; }

        internal object this[ string s ] {
            get {
                return GetValueForName( s );
            }
            set {
                SetValueForName( s, value );
            }
        }

        internal object this[ int i ] {
            get {
                var idx = Collection.LogicalMappings[ i ];
                if ( !_data.ContainsKey( idx ) ) _data[ idx ] = null;
                return _data[ idx ];
            }
            set {
                _data[ Collection.LogicalMappings[ i ] ] = value;
            }
        }

        private void SetValueForName( string colName, object value ) {
            var index = Collection.Mapping[ colName ];
            this[ index ] = value;
        }

        private object GetValueForName( string colName ) {
            var index = Collection.Mapping[ colName ];
            if ( !_data.ContainsKey( index ) ) _data[ index ] = null;
            return this[ index ];
        }

        internal void CopyRow( MySqlSchemaRow row ) {
            if ( Collection.Columns.Count != row.Collection.Columns.Count ) throw new InvalidOperationException( "column count doesn't match" );
            for ( var i = 0; i < Collection.Columns.Count; i++ ) row[ i ] = this[ i ];
        }
    }

    public class SchemaColumn {
        public string Name { get; set; }
        public Type Type { get; set; }
    }
}