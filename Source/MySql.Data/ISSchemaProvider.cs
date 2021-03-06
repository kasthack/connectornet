// Copyright � 2004, 2013, Oracle and/or its affiliates. All rights reserved.
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
using System.Data.SqlTypes;
using System.Text;
using MySql.Data.MySqlClient.Properties;
using MySql.Data.Types;
using MySql.Data.Constants.Types;
using MySql.Data.Constants.ColumnNames.Columns;
using MySql.Data.Constants.ColumnNames.Tables;
using MySql.Data.Constants.ColumnNames.Procedures;
using MySql.Data.Constants.ColumnNames.Shared;
using MySql.Data.Constants.ColumnNames.Triggers;
using MySql.Data.Constants.ColumnNames.Views;

namespace MySql.Data.MySqlClient {
    internal class IsSchemaProvider : SchemaProvider {
        private const string SpecificCatalog = "SPECIFIC_CATALOG";
        private const string SpecificSchema = "SPECIFIC_SCHEMA";
        private const string SpecificName = "SPECIFIC_NAME";

        public IsSchemaProvider( MySqlConnection connection ) : base( connection ) { }

        protected override MySqlSchemaCollection GetCollections() {
            var dt = base.GetCollections();

            var collections = new[] {
                new object[] { "Views", 2, 3 }, new object[] { "ViewColumns", 3, 4 }, new object[] { "Procedure Parameters", 5, 1 },
                new object[] { "Procedures", 4, 3 }, new object[] { "Triggers", 2, 4 }
            };

            FillTable( dt, collections );
            return dt;
        }

        protected override MySqlSchemaCollection GetRestrictions() {
            var dt = base.GetRestrictions();

            var restrictions = new[] {
                new object[] { "Procedure Parameters", "Database", "", 0 }, new object[] { "Procedure Parameters", "Schema", "", 1 },
                new object[] { "Procedure Parameters", "Name", "", 2 }, new object[] { "Procedure Parameters", "Type", "", 3 },
                new object[] { "Procedure Parameters", "Parameter", "", 4 }, new object[] { "Procedures", "Database", "", 0 },
                new object[] { "Procedures", "Schema", "", 1 }, new object[] { "Procedures", "Name", "", 2 },
                new object[] { "Procedures", "Type", "", 3 }, new object[] { "Views", "Database", "", 0 },
                new object[] { "Views", "Schema", "", 1 }, new object[] { "Views", "Table", "", 2 },
                new object[] { "ViewColumns", "Database", "", 0 }, new object[] { "ViewColumns", "Schema", "", 1 },
                new object[] { "ViewColumns", "Table", "", 2 }, new object[] { "ViewColumns", "Column", "", 3 },
                new object[] { "Triggers", "Database", "", 0 }, new object[] { "Triggers", "Schema", "", 1 },
                new object[] { "Triggers", "Name", "", 2 }, new object[] { "Triggers", "EventObjectTable", "", 3 }
            };
            FillTable( dt, restrictions );
            return dt;
        }

        public override MySqlSchemaCollection GetDatabases( string[] restrictions ) {
            var dt = Query( "SCHEMATA", "", new[] {SchemaName}, restrictions );
            dt.Columns[ 1 ].Name = "database_name";
            dt.Name = "Databases";
            return dt;
        }

        public override MySqlSchemaCollection GetTables( string[] restrictions ) {
            var dt = Query( "TABLES", "TABLE_TYPE != 'VIEW'", new[] { TableCatalog, TableSchema, TableName, TableType }, restrictions );
            dt.Name = "Tables";
            return dt;
        }

        public override MySqlSchemaCollection GetColumns( string[] restrictions ) {
            var dt = Query( "COLUMNS", null, new[ ] { TableCatalog, TableSchema, TableName, ColumnName }, restrictions );
            dt.RemoveColumn( CharacterOctetLength );
            dt.Name = "Columns";
            QuoteDefaultValues( dt );
            return dt;
        }

        private MySqlSchemaCollection GetViews( string[] restrictions ) {
            var dt = Query( "VIEWS", null, new[] { TableCatalog, TableSchema, TableName }, restrictions );
            dt.Name = "Views";
            return dt;
        }

        private MySqlSchemaCollection GetViewColumns( string[] restrictions ) {
            var where = new StringBuilder();
            var sql = new StringBuilder( "SELECT C.* FROM information_schema.columns C" );
            sql.Append( " JOIN information_schema.views V " );
            sql.Append( "ON C.table_schema=V.table_schema AND C.table_name=V.table_name " );
            if ( restrictions != null
                 && restrictions.Length >= 2
                 && restrictions[ 1 ] != null ) where.InvariantAppendFormat( "C.table_schema='{0}' ", restrictions[ 1 ] );
            if ( restrictions != null
                 && restrictions.Length >= 3
                 && restrictions[ 2 ] != null ) {
                if ( where.Length > 0 ) where.Append( "AND " );
                where.InvariantAppendFormat( "C.table_name='{0}' ", restrictions[ 2 ] );
            }
            if ( restrictions != null
                 && restrictions.Length == 4
                 && restrictions[ 3 ] != null ) {
                if ( where.Length > 0 ) where.Append( "AND " );
                where.InvariantAppendFormat( "C.column_name='{0}' ", restrictions[ 3 ] );
            }
            if ( where.Length > 0 ) sql.InvariantAppendFormat( " WHERE {0}", where );
            var dt = GetTable( sql.ToString() );
            dt.Name = "ViewColumns";
            dt.Columns[ 0 ].Name = ViewCatalog;
            dt.Columns[ 1 ].Name = ViewSchema;
            dt.Columns[ 2 ].Name = ViewName;
            QuoteDefaultValues( dt );
            return dt;
        }

        private MySqlSchemaCollection GetTriggers( string[] restrictions ) {
            var dt = Query( "TRIGGERS", null, new[ ] {TriggerCatalog,TriggerSchema,EventObjectTable,TriggerName}, restrictions );
            dt.Name = "Triggers";
            return dt;
        }

        /// <summary>
        /// Return schema information about procedures and functions
        /// Restrictions supported are:
        /// schema, name, type
        /// </summary>
        /// <param name="restrictions"></param>
        /// <returns></returns>
        public override MySqlSchemaCollection GetProcedures( string[] restrictions ) {
            try {
                if ( Connection.Settings.HasProcAccess ) return base.GetProcedures( restrictions );
            }
            catch ( MySqlException ex ) {
                if ( ex.Number == (int) MySqlErrorCode.TableAccessDenied ) Connection.Settings.HasProcAccess = false;
                else throw;
            }
            var dt = Query( "ROUTINES", null, new[] { RoutineCatalog, RoutineSchema, RoutineName, RoutineType }, restrictions );
            dt.Name = "Procedures";
            return dt;
        }

        private MySqlSchemaCollection GetProceduresWithParameters( string[] restrictions ) {
            var dt = GetProcedures( restrictions );
            dt.AddColumn( Parameterlist, TString );

            foreach ( var row in dt.Rows ) row[ Parameterlist ] = GetProcedureParameterLine( row );
            return dt;
        }

        private string GetProcedureParameterLine( MySqlSchemaRow isRow ) {
            var sql = "SHOW CREATE {0} `{1}`.`{2}`";
            sql = String.Format( sql, isRow[ RoutineType ], isRow[ RoutineSchema ], isRow[ RoutineName ] );
            var cmd = new MySqlCommand( sql, Connection );
            using ( var reader = cmd.ExecuteReader() ) {
                reader.Read();

                // if we are not the owner of this proc or have permissions
                // then we will get null for the body
                if ( reader.IsDBNull( 2 ) ) return null;

                var sqlMode = reader.GetString( 1 );

                var body = reader.GetString( 2 );
                var tokenizer = new MySqlTokenizer( body ) {
                    AnsiQuotes = sqlMode.Contains("ANSI_QUOTES"),
                    BackslashEscapes = !sqlMode.Contains( "NO_BACKSLASH_ESCAPES")
                };

                var token = tokenizer.NextToken();
                while ( token != "(" ) token = tokenizer.NextToken();
                var start = tokenizer.StartIndex + 1;
                token = tokenizer.NextToken();
                while ( token != ")"
                        || tokenizer.Quoted ) {
                    token = tokenizer.NextToken();
                    // if we see another ( and we are not quoted then we
                    // are in a size element and we need to look for the closing paren
                    if ( token == "("
                         && !tokenizer.Quoted ) {
                        while ( token != ")"
                                || tokenizer.Quoted ) token = tokenizer.NextToken();
                        token = tokenizer.NextToken();
                    }
                }
                return body.Substring( start, tokenizer.StartIndex - start );
            }
        }

        private MySqlSchemaCollection GetParametersForRoutineFromIs( string[] restrictions ) {
            var sql = new StringBuilder( @"SELECT * FROM INFORMATION_SCHEMA.PARAMETERS" );
            // now get our where clause and append it if there is one
            var where = GetWhereClause( null, new [] {SpecificCatalog,SpecificSchema,SpecificName,RoutineType,ParameterName}, restrictions );
            if ( !String.IsNullOrEmpty( where ) ) sql.InvariantAppendFormat( " WHERE {0}", where );

            var coll = QueryCollection( "parameters", sql.ToString() );

            if ( ( coll.Rows.Count != 0 )
                 && ( (string) coll.Rows[ 0 ][ "routine_type" ] == "FUNCTION" ) ) {
                // update missing data for the first row (function return value).
                // (using sames valus than GetParametersFromShowCreate).
                coll.Rows[ 0 ][ "parameter_mode" ] = "IN";
                coll.Rows[ 0 ][ "parameter_name" ] = "return_value"; // "FUNCTION";
            }
            return coll;
        }

        private MySqlSchemaCollection GetParametersFromIs( string[] restrictions, MySqlSchemaCollection routines ) {
            MySqlSchemaCollection parms = null;

            if ( routines == null
                 || routines.Rows.Count == 0 )
                parms = restrictions == null ? QueryCollection( "parameters", "SELECT * FROM INFORMATION_SCHEMA.PARAMETERS WHERE 1=2" ) : GetParametersForRoutineFromIs( restrictions );
            else
                foreach ( var routine in routines.Rows ) {
                    if ( restrictions != null
                         && restrictions.Length >= 3 ) restrictions[ 2 ] = routine[ RoutineName ].ToString();

                    parms = GetParametersForRoutineFromIs( restrictions );
                }
            parms.Name = "Procedure Parameters";
            return parms;
        }

        internal MySqlSchemaCollection CreateParametersTable() {
            var dt = new MySqlSchemaCollection( "Procedure Parameters" );
            //todo: constants overlapping with Columns -> separate class
            dt.AddColumn( SpecificCatalog, TString );
            dt.AddColumn( SpecificSchema, TString );
            dt.AddColumn( SpecificName, TString );
            dt.AddColumn( OrdinalPosition, TInt32 );
            dt.AddColumn( ParameterMode, TString );
            dt.AddColumn( ParameterName, TString );
            dt.AddColumn( DataType, TString );
            dt.AddColumn( CharacterMaximumLength, TInt32 );
            dt.AddColumn( CharacterOctetLength, TInt32 );
            dt.AddColumn( NumericPrecision, TByte );
            dt.AddColumn( NumericScale, TInt32 );
            dt.AddColumn( CharacterSetName, TString );
            dt.AddColumn( CollationName, TString );
            dt.AddColumn( DtdIdentifier, TString );
            dt.AddColumn( RoutineType, TString );
            return dt;
        }

        /// <summary>
        /// Return schema information about parameters for procedures and functions
        /// Restrictions supported are:
        /// schema, name, type, parameter name
        /// </summary>
        public virtual MySqlSchemaCollection GetProcedureParameters( string[] restrictions, MySqlSchemaCollection routines ) {
            var is55 = Connection.Driver.Version.IsAtLeast( 5, 5, 3 );

            try {
                // we want to avoid using IS if  we can as it is painfully slow
                var dt = CreateParametersTable();
                GetParametersFromShowCreate( dt, restrictions, routines );
                return dt;
            }
            catch ( Exception ) {
                if ( !is55 ) throw;

                // we get here by not having access and we are on 5.5 or later so just use IS
                return GetParametersFromIs( restrictions, routines );
            }
        }

        protected override MySqlSchemaCollection GetSchemaInternal( string collection, string[] restrictions ) {
            var dt = base.GetSchemaInternal( collection, restrictions );
            if ( dt != null ) return dt;

            switch ( collection ) {
                case "VIEWS":
                    return GetViews( restrictions );
                case "PROCEDURES":
                    return GetProcedures( restrictions );
                case "PROCEDURES WITH PARAMETERS":
                    return GetProceduresWithParameters( restrictions );
                case "PROCEDURE PARAMETERS":
                    return GetProcedureParameters( restrictions, null );
                case "TRIGGERS":
                    return GetTriggers( restrictions );
                case "VIEWCOLUMNS":
                    return GetViewColumns( restrictions );
            }
            return null;
        }

        private static string GetWhereClause( string initialWhere, string[] keys, string[] values ) {
            var where = new StringBuilder( initialWhere );
            if ( values == null ) return @where.ToString();
            for ( var i = 0; i < keys.Length; i++ ) {
                if ( i >= values.Length ) break;
                if ( values[ i ] == null
                     || values[ i ] == String.Empty ) continue;
                if ( @where.Length > 0 ) @where.Append( " AND " );
                @where.InvariantAppendFormat( "{0} LIKE '{1}'", keys[ i ], values[ i ] );
            }
            return where.ToString();
        }

        private MySqlSchemaCollection Query( string tableName, string initialWhere, string[] keys, string[] values ) {
            var query = new StringBuilder( "SELECT * FROM INFORMATION_SCHEMA." );
            query.Append( tableName );

            var where = GetWhereClause( initialWhere, keys, values );

            if ( where.Length > 0 ) query.InvariantAppendFormat( " WHERE {0}", where );

            return GetTable( query.ToString() );
        }

        private MySqlSchemaCollection GetTable( string sql ) {
            var c = new MySqlSchemaCollection();
            var cmd = new MySqlCommand( sql, Connection );
            var reader = cmd.ExecuteReader();

            // add columns
            for ( var i = 0; i < reader.FieldCount; i++ ) c.AddColumn( reader.GetName( i ), reader.GetFieldType( i ) );

            using ( reader )
                while ( reader.Read() ) {
                    var row = c.AddRow();
                    for ( var i = 0; i < reader.FieldCount; i++ ) row[ i ] = reader.GetValue( i );
                }
            return c;
        }

        public override MySqlSchemaCollection GetForeignKeys( string[] restrictions ) {
            if ( !Connection.Driver.Version.IsAtLeast( 5, 1, 16 ) ) return base.GetForeignKeys( restrictions );

            var sql = @"SELECT rc.constraint_catalog, rc.constraint_schema,
                rc.constraint_name, kcu.table_catalog, kcu.table_schema, rc.table_name,
                rc.match_option, rc.update_rule, rc.delete_rule, 
                NULL as referenced_table_catalog,
                kcu.referenced_table_schema, rc.referenced_table_name 
                FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
                LEFT JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu ON 
                kcu.constraint_catalog <=> rc.constraint_catalog AND
                kcu.constraint_schema <=> rc.constraint_schema AND 
                kcu.constraint_name <=> rc.constraint_name AND
                kcu.ORDINAL_POSITION=1 WHERE 1=1";

            var where = new StringBuilder();
            if ( restrictions.Length >= 2
                 && !String.IsNullOrEmpty( restrictions[ 1 ] ) ) where.InvariantAppendFormat( " AND rc.constraint_schema LIKE '{0}'", restrictions[ 1 ] );
            if ( restrictions.Length >= 3
                 && !String.IsNullOrEmpty( restrictions[ 2 ] ) ) where.InvariantAppendFormat( " AND rc.table_name LIKE '{0}'", restrictions[ 2 ] );
            if ( restrictions.Length >= 4
                 && !String.IsNullOrEmpty( restrictions[ 3 ] ) ) where.InvariantAppendFormat( " AND rc.constraint_name LIKE '{0}'", restrictions[ 2 ] );

            sql += where.ToString();

            return GetTable( sql );
        }

        public override MySqlSchemaCollection GetForeignKeyColumns( string[] restrictions ) {
            if ( !Connection.Driver.Version.IsAtLeast( 5, 0, 6 ) ) return base.GetForeignKeyColumns( restrictions );
            const string sql = @"SELECT kcu.* FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu WHERE kcu.referenced_table_name IS NOT NULL";
            var query = new StringBuilder(sql);
            if ( restrictions.Length >= 2 && !String.IsNullOrEmpty( restrictions[ 1 ] ) )
                query.InvariantAppendFormat( " AND kcu.constraint_schema LIKE '{0}'", restrictions[ 1 ] );
            if ( restrictions.Length >= 3 && !String.IsNullOrEmpty( restrictions[ 2 ] ) )
                query.InvariantAppendFormat( " AND kcu.table_name LIKE '{0}'", restrictions[ 2 ] );
            if ( restrictions.Length >= 4 && !String.IsNullOrEmpty( restrictions[ 3 ] ) )
                query.InvariantAppendFormat( " AND kcu.constraint_name LIKE '{0}'", restrictions[ 3 ] );
            return GetTable( query.ToString() );
        }

        #region Procedures Support Rouines
        internal void GetParametersFromShowCreate(
            MySqlSchemaCollection parametersTable,
            string[] restrictions,
            MySqlSchemaCollection routines ) {
            // this allows us to pass in a pre-populated routines table
            // and avoid the querying for them again.
            // we use this when calling a procedure or function
            if ( routines == null ) routines = GetSchema( "procedures", restrictions );

            var cmd = Connection.CreateCommand();

            foreach ( var routine in routines.Rows ) {
                var showCreateSql = String.Format(
                    "SHOW CREATE {0} `{1}`.`{2}`",
                    routine[ RoutineType ],
                    routine[ RoutineSchema ],
                    routine[ RoutineName ] );
                cmd.CommandText = showCreateSql;
                try {
                    string nameToRestrict = null;
                    if ( restrictions != null
                         && restrictions.Length == 5
                         && restrictions[ 4 ] != null ) nameToRestrict = restrictions[ 4 ];
                    using ( var reader = cmd.ExecuteReader() ) {
                        reader.Read();
                        var body = reader.GetString( 2 );
                        reader.Close();
                        ParseProcedureBody( parametersTable, body, routine, nameToRestrict );
                    }
                }
                catch ( SqlNullValueException snex )
                {
                    throw new InvalidOperationException(
                        String.Format( Resources.UnableToRetrieveParameters, routine[ RoutineName ] ),
                        snex );
                }
            }
        }

        private void ParseProcedureBody( MySqlSchemaCollection parametersTable, string body, MySqlSchemaRow row, string nameToRestrict ) {
            var modes = new List<string>( new[] { "IN", "OUT", "INOUT" } );

            var sqlMode = row[ "SQL_MODE" ].ToString();

            var pos = 1;
            var tokenizer = new MySqlTokenizer( body ) {
                AnsiQuotes = sqlMode.Contains("ANSI_QUOTES"),
                BackslashEscapes = !sqlMode.Contains( "NO_BACKSLASH_ESCAPES"),
                ReturnComments = false
            };
            var token = tokenizer.NextToken();

            // this block will scan for the opening paren while also determining
            // if this routine is a function.  If so, then we need to add a
            // parameter row for the return parameter since it is ordinal position
            // 0 and should appear first.
            while ( token != "(" ) {
                if ( token.IgnoreCaseCompare( "FUNCTION" ) == 0 && nameToRestrict == null ) {
                    parametersTable.AddRow();
                    InitParameterRow( row, parametersTable.Rows[ 0 ] );
                }
                token = tokenizer.NextToken();
            }
            token = tokenizer.NextToken(); // now move to the next token past the (

            while ( token != ")" ) {
                var parmRow = parametersTable.NewRow();
                InitParameterRow( row, parmRow );
                parmRow[ OrdinalPosition ] = pos++;

                // handle mode and name for the parameter
                var mode = token.InvariantToUpper();
                if ( !tokenizer.Quoted && modes.Contains( mode ) ) {
                    parmRow[ ParameterMode ] = mode;
                    token = tokenizer.NextToken();
                }
                if ( tokenizer.Quoted ) token = token.Substring( 1, token.Length - 2 );
                parmRow[ ParameterName ] = token;
                // now parse data type
                token = ParseDataType( parmRow, tokenizer );
                if ( token == "," ) token = tokenizer.NextToken();
                // now determine if we should include this row after all
                // we need to parse it before this check so we are correctly
                // positioned for the next parameter
                if ( nameToRestrict == null || nameToRestrict.IgnoreCaseCompare( parmRow[ ParameterName ].ToString() ) == 0 ) parametersTable.Rows.Add( parmRow );
            }
            // now parse out the return parameter if there is one.
            if ( tokenizer.NextToken().IgnoreCaseEquals( "RETURNS") ) return;
            var parameterRow = parametersTable.Rows[ 0 ];
            parameterRow[ ParameterName ] = "RETURN_VALUE";
            ParseDataType( parameterRow, tokenizer );
        }

        /// <summary>
        /// Initializes a new row for the procedure parameters table.
        /// </summary>
        private static void InitParameterRow( MySqlSchemaRow procedure, MySqlSchemaRow parameter ) {
            parameter[ SpecificCatalog ] = null;
            parameter[ SpecificSchema ] = procedure[ RoutineSchema ];
            parameter[ SpecificName ] = procedure[ RoutineName ];
            parameter[ ParameterMode ] = "IN";
            parameter[ OrdinalPosition ] = 0;
            parameter[ RoutineType ] = procedure[ RoutineType ];
        }

        /// <summary>
        ///  Parses out the elements of a procedure parameter data type.
        /// </summary>
        private string ParseDataType( MySqlSchemaRow row, MySqlTokenizer tokenizer ) {
            var dtd = new StringBuilder( tokenizer.NextToken().InvariantToUpper() );
            row[ DataType ] = dtd.ToString();
            var type = row[ DataType ].ToString();

            var token = tokenizer.NextToken();
            if ( token == "(" ) {
                token = tokenizer.ReadParenthesis();
                dtd.InvariantAppendFormat( "{0}", token );
                if ( type != "ENUM"
                     && type != "SET" ) ParseDataTypeSize( row, token );
                token = tokenizer.NextToken();
            }
            else dtd.Append( GetDataTypeDefaults( type, row ) );

            while ( token != ")" && token != "," && !token.IgnoreCaseEquals("begin") && token.IgnoreCaseEquals("return")) {
                switch ( token ) {
                    case "CHARACTER":
                    case "BINARY":
                        break;
                    case "SET":
                    case "CHARSET":
                        row[ CharacterSetName ] = tokenizer.NextToken();
                        break;
                    case "ASCII":
                        row[ CharacterSetName ] = "latin1";
                        break;
                    case "UNICODE":
                        row[ CharacterSetName ] = "ucs2";
                        break;
                    case "COLLATE":
                        row[ CollationName ] = tokenizer.NextToken();
                        break;
                    default:
                        dtd.InvariantAppendFormat( " {0}", token );
                        break;
                }
                token = tokenizer.NextToken();
            }

            if ( dtd.Length > 0 ) row[ DtdIdentifier ] = dtd.ToString();

            // now default the collation if one wasn't given
            if ( string.IsNullOrEmpty( (string) row[ CollationName ] ) && !string.IsNullOrEmpty( (string) row[ CharacterSetName ] ) )
                row[ CollationName ] = CharSetMap.GetDefaultCollation( row[ CharacterSetName ].ToString(), Connection );

            // now set the octet length
            if ( row[ CharacterMaximumLength ] == null ) return token;
            if ( row[ CharacterSetName ] == null ) row[ CharacterSetName ] = "";
            row[ CharacterOctetLength ] = CharSetMap.GetMaxLength( (string) row[ CharacterSetName ], Connection ) * (int) row[ CharacterMaximumLength ];
            return token;
        }

        private static string GetDataTypeDefaults( string type, MySqlSchemaRow row ) {
            var format = "({0},{1})";
            //todo check unused var
            var precision = row[ NumericPrecision ];
            if ( !MetaData.IsNumericType( type ) || !string.IsNullOrEmpty( (string) row[ NumericPrecision ] ) )
                return String.Empty;
            row[ NumericPrecision ] = 10;
            row[ NumericScale ] = 0;
            if ( !MetaData.SupportScale( type ) ) format = "({0})";
            return String.Format( format, row[ NumericPrecision ], row[ NumericScale ] );
        }

        private static void ParseDataTypeSize( MySqlSchemaRow row, string size ) {
            var parts = size.Trim( '(', ')' ).Split( ',' );
            if ( !MetaData.IsNumericType( row[ DataType ].ToString() ) )
                row[ CharacterMaximumLength ] = Int32.Parse( parts[ 0 ] );
            // will set octet length in a minute
            else {
                row[ NumericPrecision ] = Int32.Parse( parts[ 0 ] );
                if ( parts.Length == 2 ) row[ NumericScale ] = Int32.Parse( parts[ 1 ] );
            }
        }
        #endregion
    }
}