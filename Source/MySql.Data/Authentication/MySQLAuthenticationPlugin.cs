﻿// Copyright © 2012, Oracle and/or its affiliates. All rights reserved.
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
using System.Diagnostics;
using System.Text;
using MySql.Data.MySqlClient.Properties;

namespace MySql.Data.MySqlClient.Authentication {
    public abstract class MySqlAuthenticationPlugin {
        private NativeDriver _driver;
        protected byte[] AuthenticationData;

        /// <summary>
        /// This is a factory method that is used only internally.  It creates an auth plugin based on the method type
        /// </summary>
        /// <param name="method"></param>
        /// <param name="flags"></param>
        /// <param name="settings"></param>
        /// <returns></returns>
        internal static MySqlAuthenticationPlugin GetPlugin( string method, NativeDriver driver, byte[] authData ) {
            if ( method == "mysql_old_password" ) {
                driver.Close( true );
                throw new MySqlException( Resources.OldPasswordsNotSupported );
            }
            var plugin = AuthenticationPluginManager.GetPlugin( method );
            if ( plugin == null ) throw new MySqlException( String.Format( Resources.UnknownAuthenticationMethod, method ) );
            plugin._driver = driver;
            plugin.SetAuthData( authData );
            return plugin;
        }

        protected MySqlConnectionStringBuilder Settings => _driver.Settings;

        protected Version ServerVersion => new Version( _driver.Version.Major, _driver.Version.Minor, _driver.Version.Build );

        internal ClientFlags Flags => _driver.Flags;

        protected Encoding Encoding => _driver.Encoding;

        protected virtual void SetAuthData( byte[] data ) { AuthenticationData = data; }

        protected virtual void CheckConstraints() { }

        protected virtual void AuthenticationFailed( Exception ex ) {
            throw new MySqlException( String.Format( Resources.AuthenticationFailed, Settings.Server, GetUsername(), PluginName, ex.Message ), ex );
        }
        protected virtual void AuthenticationSuccessful() { }
        protected virtual byte[] MoreData( byte[] data ) => null;
        internal void Authenticate( bool reset ) {
            CheckConstraints();
            var packet = _driver.Packet;
            // send auth response
            packet.WriteString( GetUsername() );
            // now write the password
            WritePassword( packet );
            if ( ( Flags & ClientFlags.ConnectWithDb ) != 0 || reset )
                if ( !String.IsNullOrWhiteSpace( Settings.Database ) )
                    packet.WriteString( Settings.Database );
            if ( reset ) packet.WriteInteger( 8, 2 );
            if ( ( Flags & ClientFlags.PluginAuth ) != 0 ) packet.WriteString( PluginName );
            _driver.SetConnectAttrs();
            _driver.SendPacket( packet );
            //read server response
            packet = ReadPacket();
            var b = packet.Buffer;
            if ( b[ 0 ] == 0xfe ) {
                if ( packet.IsLastPacket ) {
                    _driver.Close( true );
                    throw new MySqlException( Resources.OldPasswordsNotSupported );
                }
                HandleAuthChange( packet );
            }
            _driver.ReadOk( false );
            AuthenticationSuccessful();
        }

        private void WritePassword( MySqlPacket packet ) {
            var secure = ( Flags & ClientFlags.SecureConnection ) != 0;
            var password = GetPassword();
            var s = password as string;
            if ( s != null )
                if ( secure ) packet.WriteLenString( s );
                else packet.WriteString( s );
            else if ( password == null ) packet.WriteByte( 0 );
            else if ( password is byte[] ) packet.Write( password as byte[] );
            else throw new MySqlException( string.Format( "Unexpected password format: {0}", password.GetType() ) );
        }

        private MySqlPacket ReadPacket() {
            try {
                return _driver.ReadPacket();
            }
            catch ( MySqlException ex ) {
                // make sure this is an auth failed ex
                AuthenticationFailed( ex );
                return null;
            }
        }
        private void HandleAuthChange( MySqlPacket packet ) {
            Debug.Assert( packet.ReadByte() == 0xfe );
            var method = packet.ReadString();
            var authData = new byte[packet.Length - packet.Position];
            Array.Copy( packet.Buffer, packet.Position, authData, 0, authData.Length );
            var plugin = GetPlugin( method, _driver, authData );
            plugin.AuthenticationChange();
        }

        private void AuthenticationChange() {
            var packet = _driver.Packet;
            packet.Clear();
            var moreData = MoreData( null );
            while ( moreData != null && moreData.Length > 0 ) {
                packet.Clear();
                packet.Write( moreData );
                _driver.SendPacket( packet );
                packet = ReadPacket();
                var prefixByte = packet.Buffer[ 0 ];
                if ( prefixByte != 1 ) return;
                // a prefix of 0x01 means need more auth data
                var responseData = new byte[packet.Length - 1];
                Array.Copy( packet.Buffer, 1, responseData, 0, responseData.Length );
                moreData = MoreData( responseData );
            }
            // we get here if MoreData returned null but the last packet read was a more data packet
            ReadPacket();
        }
        public abstract string PluginName { get; }
        public virtual string GetUsername() => Settings.UserId;
        public virtual object GetPassword() => null;
    }
}