// Copyright � 2004, 2014, Oracle and/or its affiliates. All rights reserved.
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
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Drawing;
using System.Threading.Tasks;
using System.Threading;

namespace MySql.Data.MySqlClient {
    /// <include file='docs/MySqlDataAdapter.xml' path='docs/class/*'/>
    [ToolboxBitmap( typeof( MySqlDataAdapter ), "MySqlClient.resources.dataadapter.bmp" )]
    [DesignerCategory( "Code" )]
    [Designer( "MySql.Data.MySqlClient.Design.MySqlDataAdapterDesigner,MySqlClient.Design" )]
    public sealed class MySqlDataAdapter : DbDataAdapter {
        private int _updateBatchSize;
        private List<IDbCommand> _commandBatch;

        /// <summary>
        /// Occurs during Update before a command is executed against the data source. The attempt to update is made, so the event fires.
        /// </summary>
        public event MySqlRowUpdatingEventHandler RowUpdating;

        /// <summary>
        /// Occurs during Update after a command is executed against the data source. The attempt to update is made, so the event fires.
        /// </summary>
        public event MySqlRowUpdatedEventHandler RowUpdated;

        /// <include file='docs/MySqlDataAdapter.xml' path='docs/Ctor/*'/>
        public MySqlDataAdapter() {
            LoadDefaults = true;
            _updateBatchSize = 1;
        }

        /// <include file='docs/MySqlDataAdapter.xml' path='docs/Ctor1/*'/>
        public MySqlDataAdapter( MySqlCommand selectCommand ) : this() {
            SelectCommand = selectCommand;
        }

        /// <include file='docs/MySqlDataAdapter.xml' path='docs/Ctor2/*'/>
        public MySqlDataAdapter( string selectCommandText, MySqlConnection connection ) : this() {
            SelectCommand = new MySqlCommand( selectCommandText, connection );
        }

        /// <include file='docs/MySqlDataAdapter.xml' path='docs/Ctor3/*'/>
        public MySqlDataAdapter( string selectCommandText, string selectConnString ) : this() {
            SelectCommand = new MySqlCommand( selectCommandText, new MySqlConnection( selectConnString ) );
        }

        #region Properties
        /// <include file='docs/MySqlDataAdapter.xml' path='docs/DeleteCommand/*'/>
        [Description( "Used during Update for deleted rows in Dataset." )]
            public new MySqlCommand DeleteCommand {
            get {
                return (MySqlCommand) base.DeleteCommand;
            }
            set {
                base.DeleteCommand = value;
            }
        }

        /// <include file='docs/MySqlDataAdapter.xml' path='docs/InsertCommand/*'/>
        [Description( "Used during Update for new rows in Dataset." )]
            public new MySqlCommand InsertCommand {
            get {
                return (MySqlCommand) base.InsertCommand;
            }
            set {
                base.InsertCommand = value;
            }
        }

        /// <include file='docs/MySqlDataAdapter.xml' path='docs/SelectCommand/*'/>
        [Description( "Used during Fill/FillSchema" )]
        [Category( "Fill" )]
            public new MySqlCommand SelectCommand {
            get {
                return (MySqlCommand) base.SelectCommand;
            }
            set {
                base.SelectCommand = value;
            }
        }

        /// <include file='docs/MySqlDataAdapter.xml' path='docs/UpdateCommand/*'/>
        [Description( "Used during Update for modified rows in Dataset." )]
            public new MySqlCommand UpdateCommand {
            get {
                return (MySqlCommand) base.UpdateCommand;
            }
            set {
                base.UpdateCommand = value;
            }
        }

        internal bool LoadDefaults { get; set; }
        #endregion

        /// <summary>
        /// Open connection if it was closed.
        /// Necessary to workaround "connection must be open and valid" error
        /// with batched updates.
        /// </summary>
        /// <param name="state">Row state</param>
        /// <param name="openedConnections"> list of opened connections 
        /// If connection is opened by this function, the list is updated
        /// </param>
        /// <returns>true if connection was opened</returns>
        private void OpenConnectionIfClosed( DataRowState state, List<MySqlConnection> openedConnections ) {
            MySqlCommand cmd = null;
            switch ( state ) {
                case DataRowState.Added:
                    cmd = InsertCommand;
                    break;
                case DataRowState.Deleted:
                    cmd = DeleteCommand;
                    break;
                case DataRowState.Modified:
                    cmd = UpdateCommand;
                    break;
                default:
                    return;
            }

            if ( cmd?.Connection?.ConnectionState != ConnectionState.Closed ) return;
            cmd.Connection.Open();
            openedConnections.Add( cmd.Connection );
        }

        protected override int Update( DataRow[] dataRows, DataTableMapping tableMapping ) {
            var connectionsOpened = new List<MySqlConnection>();
            try {
                // Open connections for insert/update/update commands, if 
                // connections are closed.
                foreach ( var row in dataRows ) OpenConnectionIfClosed( row.RowState, connectionsOpened );
                return base.Update( dataRows, tableMapping );
            }
            finally {
                foreach ( var c in connectionsOpened ) c.Close();
            }
        }

        #region Batching Support
        public override int UpdateBatchSize {
            get {
                return _updateBatchSize;
            }
            set {
                _updateBatchSize = value;
            }
        }

        protected override void InitializeBatching() { _commandBatch = new List<IDbCommand>(); }

        protected override int AddToBatch( IDbCommand command ) {
            // the first time each command is asked to be batched, we ask
            // that command to prepare its batchable command text.  We only want
            // to do this one time for each command
            var commandToBatch = (MySqlCommand) command;
            if ( commandToBatch.BatchableCommandText == null ) commandToBatch.GetCommandTextForBatching();
            _commandBatch.Add( (IDbCommand) ( (ICloneable) command ).Clone() );
            return _commandBatch.Count - 1;
        }

        protected override int ExecuteBatch() {
            var recordsAffected = 0;
            var index = 0;
            while ( index < _commandBatch.Count ) {
                var cmd = (MySqlCommand) _commandBatch[ index++ ];
                for ( var index2 = index; index2 < _commandBatch.Count; index2++, index++ ) {
                    var cmd2 = (MySqlCommand) _commandBatch[ index2 ];
                    if ( cmd2.BatchableCommandText == null
                         || cmd2.CommandText != cmd.CommandText ) break;
                    cmd.AddToBatch( cmd2 );
                }
                recordsAffected += cmd.ExecuteNonQuery();
            }
            return recordsAffected;
        }

        protected override void ClearBatch() {
            if ( _commandBatch.Count > 0 ) ( (MySqlCommand) _commandBatch[ 0 ] ).Batch?.Clear();
            _commandBatch.Clear();
        }

        protected override void TerminateBatching() {
            ClearBatch();
            _commandBatch = null;
        }

        protected override IDataParameter GetBatchedParameter( int commandIdentifier, int parameterIndex )
            => (IDataParameter) _commandBatch[ commandIdentifier ].Parameters[ parameterIndex ];
        #endregion

        /// <summary>
        /// Overridden. See <see cref="DbDataAdapter.CreateRowUpdatedEvent"/>.
        /// </summary>
        /// <param name="dataRow"></param>
        /// <param name="command"></param>
        /// <param name="statementType"></param>
        /// <param name="tableMapping"></param>
        /// <returns></returns>
        protected override RowUpdatedEventArgs CreateRowUpdatedEvent(
            DataRow dataRow,
            IDbCommand command,
            StatementType statementType,
            DataTableMapping tableMapping ) => new MySqlRowUpdatedEventArgs( dataRow, command, statementType, tableMapping );

        /// <summary>
        /// Overridden. See <see cref="DbDataAdapter.CreateRowUpdatingEvent"/>.
        /// </summary>
        /// <param name="dataRow"></param>
        /// <param name="command"></param>
        /// <param name="statementType"></param>
        /// <param name="tableMapping"></param>
        /// <returns></returns>
        protected override RowUpdatingEventArgs CreateRowUpdatingEvent(
            DataRow dataRow,
            IDbCommand command,
            StatementType statementType,
            DataTableMapping tableMapping ) => new MySqlRowUpdatingEventArgs( dataRow, command, statementType, tableMapping );

        /// <summary>
        /// Overridden. Raises the RowUpdating event.
        /// </summary>
        /// <param name="value">A MySqlRowUpdatingEventArgs that contains the event data.</param>
        protected override void OnRowUpdating( RowUpdatingEventArgs value ) => RowUpdating?.Invoke( this, ( value as MySqlRowUpdatingEventArgs ) );

        /// <summary>
        /// Overridden. Raises the RowUpdated event.
        /// </summary>
        /// <param name="value">A MySqlRowUpdatedEventArgs that contains the event data. </param>
        protected override void OnRowUpdated( RowUpdatedEventArgs value ) => RowUpdated?.Invoke( this, ( value as MySqlRowUpdatedEventArgs ) );

        #region Async

        #region Fill
        /// <summary>
        /// Async version of Fill
        /// </summary>
        /// <param name="dataSet">Dataset to use</param>
        /// <returns>int</returns>
        public Task<int> FillAsync( DataSet dataSet ) => FillAsync( dataSet, CancellationToken.None );

        public Task<int> FillAsync( DataSet dataSet, CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<int>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    result.SetResult( base.Fill( dataSet ) );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of Fill
        /// </summary>
        /// <param name="dataTable">Datatable to use</param>
        /// <returns>int</returns>
        public Task<int> FillAsync( DataTable dataTable ) => FillAsync( dataTable, CancellationToken.None );

        public Task<int> FillAsync( DataTable dataTable, CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<int>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    var fillResult = base.Fill( dataTable );
                    result.SetResult( fillResult );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of Fill
        /// </summary>
        /// <param name="dataSet">DataSet to use</param>
        /// <param name="srcTable">Source table</param>
        /// <returns>int</returns>
        public Task<int> FillAsync( DataSet dataSet, string srcTable ) => FillAsync( dataSet, srcTable, CancellationToken.None );

        public Task<int> FillAsync( DataSet dataSet, string srcTable, CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<int>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    result.SetResult( base.Fill( dataSet, srcTable ) );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of Fill
        /// </summary>
        /// <param name="dataTable">Datatable to use</param>
        /// <param name="dataReader">DataReader to use</param>
        /// <returns>int</returns>
        public Task<int> FillAsync( DataTable dataTable, IDataReader dataReader ) => FillAsync( dataTable, dataReader, CancellationToken.None );

        public Task<int> FillAsync( DataTable dataTable, IDataReader dataReader, CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<int>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    result.SetResult( base.Fill( dataTable, dataReader ) );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of Fill
        /// </summary>
        /// <param name="dataTable">DataTable to use</param>
        /// <param name="command">DbCommand to use</param>
        /// <param name="behavior">Command Behavior</param>
        /// <returns>int</returns>
        public Task<int> FillAsync( DataTable dataTable, IDbCommand command, CommandBehavior behavior ) => FillAsync( dataTable, command, behavior, CancellationToken.None );

        public Task<int> FillAsync( DataTable dataTable, IDbCommand command, CommandBehavior behavior, CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<int>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    var fillResult = base.Fill( dataTable, command, behavior );
                    result.SetResult( fillResult );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of Fill
        /// </summary>
        /// <param name="startRecord">Start record</param>
        /// <param name="maxRecords">Max records</param>
        /// <param name="dataTables">DataTable[] to use</param>
        /// <returns>int</returns>
        public Task<int> FillAsync( int startRecord, int maxRecords, params DataTable[] dataTables ) => FillAsync( startRecord, maxRecords, CancellationToken.None, dataTables );

        public Task<int> FillAsync( int startRecord, int maxRecords, CancellationToken cancellationToken, params DataTable[] dataTables ) {
            var result = new TaskCompletionSource<int>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    result.SetResult( base.Fill( startRecord, maxRecords, dataTables ) );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of Fill
        /// </summary>
        /// <param name="dataSet">DataSet to use</param>
        /// <param name="startRecord">Start record</param>
        /// <param name="maxRecords">Max records</param>
        /// <param name="srcTable">Source table</param>
        /// <returns>int</returns>
        public Task<int> FillAsync( DataSet dataSet, int startRecord, int maxRecords, string srcTable ) => FillAsync( dataSet, startRecord, maxRecords, srcTable, CancellationToken.None );

        public Task<int> FillAsync( DataSet dataSet, int startRecord, int maxRecords, string srcTable, CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<int>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    result.SetResult( base.Fill( dataSet, startRecord, maxRecords, srcTable ) );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of Fill
        /// </summary>
        /// <param name="dataSet">DataSet to use</param>
        /// <param name="srcTable">Source table</param>
        /// <param name="dataReader">DataReader to use</param>
        /// <param name="startRecord">Start record</param>
        /// <param name="maxRecords">Max records</param>
        /// <returns></returns>
        public Task<int> FillAsync( DataSet dataSet, string srcTable, IDataReader dataReader, int startRecord, int maxRecords ) => FillAsync( dataSet, srcTable, dataReader, startRecord, maxRecords, CancellationToken.None );

        public Task<int> FillAsync(
            DataSet dataSet,
            string srcTable,
            IDataReader dataReader,
            int startRecord,
            int maxRecords,
            CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<int>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    result.SetResult( base.Fill( dataSet, srcTable, dataReader, startRecord, maxRecords ) );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of Fill
        /// </summary>
        /// <param name="dataTables">DataTable[] to use</param>
        /// <param name="startRecord">Start record</param>
        /// <param name="maxRecords">Max records</param>
        /// <param name="command">DbCommand to use</param>
        /// <param name="behavior">Command Behavior</param>
        /// <returns></returns>
        public Task<int> FillAsync( DataTable[] dataTables, int startRecord, int maxRecords, IDbCommand command, CommandBehavior behavior ) => FillAsync( dataTables, startRecord, maxRecords, command, behavior, CancellationToken.None );

        public Task<int> FillAsync(
            DataTable[] dataTables,
            int startRecord,
            int maxRecords,
            IDbCommand command,
            CommandBehavior behavior,
            CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<int>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    result.SetResult( base.Fill( dataTables, startRecord, maxRecords, command, behavior ) );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of Fill
        /// </summary>
        /// <param name="dataSet">DataSet to use</param>
        /// <param name="startRecord">Start record</param>
        /// <param name="maxRecords">Max records</param>
        /// <param name="srcTable">Source table</param>
        /// <param name="command">DbCommand to use</param>
        /// <param name="behavior">Command Behavior</param>
        /// <returns></returns>
        public Task<int> FillAsync(
            DataSet dataSet,
            int startRecord,
            int maxRecords,
            string srcTable,
            IDbCommand command,
            CommandBehavior behavior ) => FillAsync( dataSet, startRecord, maxRecords, srcTable, command, behavior, CancellationToken.None );

        public Task<int> FillAsync(
            DataSet dataSet,
            int startRecord,
            int maxRecords,
            string srcTable,
            IDbCommand command,
            CommandBehavior behavior,
            CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<int>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    result.SetResult( base.Fill( dataSet, startRecord, maxRecords, srcTable, command, behavior ) );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }
        #endregion

        #region FillSchema
        /// <summary>
        /// Async version of FillSchema
        /// </summary>
        /// <param name="dataSet">DataSet to use</param>
        /// <param name="schemaType">Schema Type</param>
        /// <returns>DataTable[]</returns>
        public Task<DataTable[]> FillSchemaAsync( DataSet dataSet, SchemaType schemaType ) => FillSchemaAsync( dataSet, schemaType, CancellationToken.None );

        public Task<DataTable[]> FillSchemaAsync( DataSet dataSet, SchemaType schemaType, CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<DataTable[]>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    result.SetResult( base.FillSchema( dataSet, schemaType ) );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of FillSchema
        /// </summary>
        /// <param name="dataSet">DataSet to use</param>
        /// <param name="schemaType">Schema Type</param>
        /// <param name="srcTable">Source Table</param>
        /// <returns>DataTable[]</returns>
        public Task<DataTable[]> FillSchemaAsync( DataSet dataSet, SchemaType schemaType, string srcTable ) => FillSchemaAsync( dataSet, schemaType, srcTable, CancellationToken.None );

        public Task<DataTable[]> FillSchemaAsync(
            DataSet dataSet,
            SchemaType schemaType,
            string srcTable,
            CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<DataTable[]>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    result.SetResult( base.FillSchema( dataSet, schemaType, srcTable ) );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of FillSchema
        /// </summary>
        /// <param name="dataSet">DataSet to use</param>
        /// <param name="schemaType">Schema Type</param>
        /// <param name="srcTable">Source Table</param>
        /// <param name="dataReader">DataReader to use</param>
        /// <returns>DataTable[]</returns>
        public Task<DataTable[]> FillSchemaAsync( DataSet dataSet, SchemaType schemaType, string srcTable, IDataReader dataReader ) => FillSchemaAsync( dataSet, schemaType, srcTable, dataReader, CancellationToken.None );

        public Task<DataTable[]> FillSchemaAsync(
            DataSet dataSet,
            SchemaType schemaType,
            string srcTable,
            IDataReader dataReader,
            CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<DataTable[]>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    var schemaResult = base.FillSchema( dataSet, schemaType, srcTable, dataReader );
                    result.SetResult( schemaResult );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of FillSchema
        /// </summary>
        /// <param name="dataSet">DataSet to use</param>
        /// <param name="schemaType">Schema Type</param>
        /// <param name="command">DBCommand to use</param>
        /// <param name="srcTable">Source Table</param>
        /// <param name="behavior">Command Behavior</param>
        /// <returns>DataTable[]</returns>
        public Task<DataTable[]> FillSchemaAsync(
            DataSet dataSet,
            SchemaType schemaType,
            IDbCommand command,
            string srcTable,
            CommandBehavior behavior ) => FillSchemaAsync( dataSet, schemaType, command, srcTable, behavior, CancellationToken.None );

        public Task<DataTable[]> FillSchemaAsync(
            DataSet dataSet,
            SchemaType schemaType,
            IDbCommand command,
            string srcTable,
            CommandBehavior behavior,
            CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<DataTable[]>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    result.SetResult( base.FillSchema( dataSet, schemaType, command, srcTable, behavior ) );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of FillSchema
        /// </summary>
        /// <param name="dataTable">DataTable to use</param>
        /// <param name="schemaType">Schema Type</param>
        /// <returns>DataTable</returns>
        public Task<DataTable> FillSchemaAsync( DataTable dataTable, SchemaType schemaType ) => FillSchemaAsync( dataTable, schemaType, CancellationToken.None );

        public Task<DataTable> FillSchemaAsync( DataTable dataTable, SchemaType schemaType, CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<DataTable>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    result.SetResult( base.FillSchema( dataTable, schemaType ) );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of FillSchema
        /// </summary>
        /// <param name="dataTable">DataTable to use</param>
        /// <param name="schemaType">Schema Type</param>
        /// <param name="dataReader">DataReader to use</param>
        /// <returns>DataTable</returns>
        public Task<DataTable> FillSchemaAsync( DataTable dataTable, SchemaType schemaType, IDataReader dataReader ) => FillSchemaAsync( dataTable, schemaType, dataReader, CancellationToken.None );

        public Task<DataTable> FillSchemaAsync(
            DataTable dataTable,
            SchemaType schemaType,
            IDataReader dataReader,
            CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<DataTable>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    result.SetResult( base.FillSchema( dataTable, schemaType, dataReader ) );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of FillSchema
        /// </summary>
        /// <param name="dataTable">DataTable to use</param>
        /// <param name="schemaType">Schema Type</param>
        /// <param name="command">DBCommand to use</param>
        /// <param name="behavior">Command Behavior</param>
        /// <returns>DataTable</returns>
        public Task<DataTable> FillSchemaAsync( DataTable dataTable, SchemaType schemaType, IDbCommand command, CommandBehavior behavior ) => FillSchemaAsync( dataTable, schemaType, command, behavior, CancellationToken.None );

        public Task<DataTable> FillSchemaAsync(
            DataTable dataTable,
            SchemaType schemaType,
            IDbCommand command,
            CommandBehavior behavior,
            CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<DataTable>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    result.SetResult( base.FillSchema( dataTable, schemaType, command, behavior ) );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }
        #endregion

        #region Update
        /// <summary>
        /// Async version of Update
        /// </summary>
        /// <param name="dataRows">DataRow[] to use</param>
        /// <returns>int</returns>
        public Task<int> UpdateAsync( DataRow[] dataRows ) => UpdateAsync( dataRows, CancellationToken.None );

        public Task<int> UpdateAsync( DataRow[] dataRows, CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<int>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    var update = base.Update( dataRows );
                    result.SetResult( update );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of Update
        /// </summary>
        /// <param name="dataSet">DataSet to use</param>
        /// <returns>int</returns>
        public Task<int> UpdateAsync( DataSet dataSet ) => UpdateAsync( dataSet, CancellationToken.None );

        public Task<int> UpdateAsync( DataSet dataSet, CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<int>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    result.SetResult( base.Update( dataSet ) );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of Update
        /// </summary>
        /// <param name="dataTable">DataTable to use</param>
        /// <returns>int</returns>
        public Task<int> UpdateAsync( DataTable dataTable ) => UpdateAsync( dataTable, CancellationToken.None );

        public Task<int> UpdateAsync( DataTable dataTable, CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<int>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    result.SetResult( base.Update( dataTable ) );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of Update
        /// </summary>
        /// <param name="dataRows">DataRow[] to use</param>
        /// <param name="tableMapping">Data Table Mapping</param>
        /// <returns>int</returns>
        public Task<int> UpdateAsync( DataRow[] dataRows, DataTableMapping tableMapping ) => UpdateAsync( dataRows, tableMapping, CancellationToken.None );

        public Task<int> UpdateAsync( DataRow[] dataRows, DataTableMapping tableMapping, CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<int>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    result.SetResult( base.Update( dataRows, tableMapping ) );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }

        /// <summary>
        /// Async version of Update
        /// </summary>
        /// <param name="dataSet">DataSet to use</param>
        /// <param name="srcTable">Source Table</param>
        /// <returns></returns>
        public Task<int> UpdateAsync( DataSet dataSet, string srcTable ) => UpdateAsync( dataSet, srcTable, CancellationToken.None );

        public Task<int> UpdateAsync( DataSet dataSet, string srcTable, CancellationToken cancellationToken ) {
            var result = new TaskCompletionSource<int>();
            if ( cancellationToken == CancellationToken.None
                 || !cancellationToken.IsCancellationRequested )
                try {
                    result.SetResult( base.Update( dataSet, srcTable ) );
                }
                catch ( Exception ex ) {
                    result.SetException( ex );
                }
            else result.SetCanceled();
            return result.Task;
        }
        #endregion

        #endregion

    }

    /// <summary>
    /// Represents the method that will handle the <see cref="MySqlDataAdapter.RowUpdating"/> event of a <see cref="MySqlDataAdapter"/>.
    /// </summary>
    public delegate void MySqlRowUpdatingEventHandler( object sender, MySqlRowUpdatingEventArgs e );

    /// <summary>
    /// Represents the method that will handle the <see cref="MySqlDataAdapter.RowUpdated"/> event of a <see cref="MySqlDataAdapter"/>.
    /// </summary>
    public delegate void MySqlRowUpdatedEventHandler( object sender, MySqlRowUpdatedEventArgs e );

    /// <summary>
    /// Provides data for the RowUpdating event. This class cannot be inherited.
    /// </summary>
    public sealed class MySqlRowUpdatingEventArgs : RowUpdatingEventArgs {
        /// <summary>
        /// Initializes a new instance of the MySqlRowUpdatingEventArgs class.
        /// </summary>
        /// <param name="row">The <see cref="DataRow"/> to 
        /// <see cref="DbDataAdapter.Update(DataSet)"/>.</param>
        /// <param name="command">The <see cref="IDbCommand"/> to execute during <see cref="DbDataAdapter.Update(DataSet)"/>.</param>
        /// <param name="statementType">One of the <see cref="StatementType"/> values that specifies the type of query executed.</param>
        /// <param name="tableMapping">The <see cref="DataTableMapping"/> sent through an <see cref="DbDataAdapter.Update(DataSet)"/>.</param>
        public MySqlRowUpdatingEventArgs( DataRow row, IDbCommand command, StatementType statementType, DataTableMapping tableMapping )
            : base( row, command, statementType, tableMapping ) {}

        /// <summary>
        /// Gets or sets the MySqlCommand to execute when performing the Update.
        /// </summary>
        public new MySqlCommand Command {
            get {
                return (MySqlCommand) base.Command;
            }
            set {
                base.Command = value;
            }
        }
    }

    /// <summary>
    /// Provides data for the RowUpdated event. This class cannot be inherited.
    /// </summary>
    public sealed class MySqlRowUpdatedEventArgs : RowUpdatedEventArgs {
        /// <summary>
        /// Initializes a new instance of the MySqlRowUpdatedEventArgs class.
        /// </summary>
        /// <param name="row">The <see cref="DataRow"/> sent through an <see cref="DbDataAdapter.Update(DataSet)"/>.</param>
        /// <param name="command">The <see cref="IDbCommand"/> executed when <see cref="DbDataAdapter.Update(DataSet)"/> is called.</param>
        /// <param name="statementType">One of the <see cref="StatementType"/> values that specifies the type of query executed.</param>
        /// <param name="tableMapping">The <see cref="DataTableMapping"/> sent through an <see cref="DbDataAdapter.Update(DataSet)"/>.</param>
        public MySqlRowUpdatedEventArgs( DataRow row, IDbCommand command, StatementType statementType, DataTableMapping tableMapping )
            : base( row, command, statementType, tableMapping ) {}

        /// <summary>
        /// Gets or sets the MySqlCommand executed when Update is called.
        /// </summary>
        public new MySqlCommand Command => (MySqlCommand) base.Command;
    }
}