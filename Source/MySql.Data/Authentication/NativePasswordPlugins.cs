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
using System.Security.Cryptography;
using AliasText = System.Text;
namespace MySql.Data.MySqlClient.Authentication {
    public class MySqlNativePasswordPlugin : MySqlAuthenticationPlugin {
        public override string PluginName => "mysql_native_password";
        protected override void SetAuthData( byte[] data ) {
            // if the data given to us is a null terminated string, we need to trim off the trailing zero
            if ( data[ data.Length - 1 ] == 0 ) {
                var b = new byte[data.Length - 1];
                Buffer.BlockCopy( data, 0, b, 0, data.Length - 1 );
                base.SetAuthData( b );
            }
            else base.SetAuthData( data );
        }
        protected override byte[] MoreData( byte[] data ) {
            var passBytes = GetPassword() as byte[];
            var buffer = new byte[passBytes.Length - 1];
            Array.Copy( passBytes, 1, buffer, 0, passBytes.Length - 1 );
            return buffer;
        }
        public override object GetPassword() {
            var bytes = Get411Password( Settings.Password, AuthenticationData );
            if ( bytes?[ 0 ] == 0 && bytes.Length == 1 ) return null;
            return bytes;
        }

        /// <summary>
        /// Returns a byte array containing the proper encryption of the 
        /// given password/seed according to the new 4.1.1 authentication scheme.
        /// </summary>
        /// <param name="password"></param>
        /// <param name="seed"></param>
        /// <returns></returns>
        private byte[] Get411Password( string password, byte[] seedBytes ) {
            // if we have no password, then we just return 1 zero byte
            if ( password.Length == 0 ) return new byte[1];
            byte[] firstHash;
            byte[] thirdHash;
            using ( var sha = new SHA1CryptoServiceProvider() ) {
                firstHash = sha.ComputeHash( AliasText.Encoding.Default.GetBytes( password ) );
                var secondHash = sha.ComputeHash( firstHash );
                var input = new byte[seedBytes.Length + secondHash.Length];
                Array.Copy( seedBytes, 0, input, 0, seedBytes.Length );
                Array.Copy( secondHash, 0, input, seedBytes.Length, secondHash.Length );
                thirdHash = sha.ComputeHash( input );
            }
            var finalHash = new byte[thirdHash.Length + 1];
            finalHash[ 0 ] = 0x14;
            Array.Copy( thirdHash, 0, finalHash, 1, thirdHash.Length );
            for ( var i = 1; i < finalHash.Length; i++ )
                finalHash[ i ] = (byte) ( finalHash[ i ] ^ firstHash[ i - 1 ] );
            return finalHash;
        }
    }
}