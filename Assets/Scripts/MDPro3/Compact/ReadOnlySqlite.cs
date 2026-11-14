using System;
using System.Data;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MDPro3.Compact.Sqlite
{
    // Deliberately limited to the SELECT/IDataReader APIs used by the three card database loaders.
    // No managed callbacks, dynamic native libraries, SQL functions, or reflection are required on iOS.
    internal static class Native
    {
#if UNITY_IOS && !UNITY_EDITOR
        private const string Library = "__Internal";
#else
        private const string Library = "sqlite3";
#endif
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_open_v2(byte[] name, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr stmt, IntPtr tail);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_step(IntPtr stmt);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_count(IntPtr stmt);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_type(IntPtr stmt, int column);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern long sqlite3_column_int64(IntPtr stmt, int column);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern double sqlite3_column_double(IntPtr stmt, int column);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_column_text(IntPtr stmt, int column);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_column_blob(IntPtr stmt, int column);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int sqlite3_column_bytes(IntPtr stmt, int column);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_column_name(IntPtr stmt, int column);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr sqlite3_errmsg(IntPtr db);
        internal static byte[] Utf8(string text) { return Encoding.UTF8.GetBytes(text + "\0"); }
        internal static string Text(IntPtr pointer, int length = -1)
        {
            if (pointer == IntPtr.Zero) return string.Empty;
            if (length < 0) { length = 0; while (Marshal.ReadByte(pointer, length) != 0) length++; }
            var bytes = new byte[length]; Marshal.Copy(pointer, bytes, 0, length); return Encoding.UTF8.GetString(bytes);
        }
    }

    public sealed class SqliteConnection : IDisposable
    {
        private readonly string path;
        internal IntPtr Handle { get; private set; }
        public SqliteConnection(string connectionString)
        {
            const string prefix = "Data Source=";
            if (!connectionString.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Expected Data Source=path");
            path = connectionString.Substring(prefix.Length);
        }
        public void Open()
        {
            if (Handle != IntPtr.Zero) throw new InvalidOperationException("Database already open.");
            IntPtr db;
            int result = Native.sqlite3_open_v2(Native.Utf8(path), out db, 0x00000001 | 0x00010000, IntPtr.Zero);
            if (result != 0)
            {
                string message = db == IntPtr.Zero ? "Cannot open card database." : Native.Text(Native.sqlite3_errmsg(db));
                if (db != IntPtr.Zero) Native.sqlite3_close_v2(db);
                throw new IOException(message);
            }
            Handle = db;
        }
        public void Close() { if (Handle != IntPtr.Zero) { Native.sqlite3_close_v2(Handle); Handle = IntPtr.Zero; } }
        public void Dispose() { Close(); }
    }

    public sealed class SqliteCommand : IDbCommand
    {
        private readonly SqliteConnection connection;
        public SqliteCommand(string sql, SqliteConnection connection) { CommandText = sql; this.connection = connection; }
        public string CommandText { get; set; }
        public IDataReader ExecuteReader() { return ExecuteReader(CommandBehavior.Default); }
        public IDataReader ExecuteReader(CommandBehavior behavior)
        {
            if (connection.Handle == IntPtr.Zero) throw new InvalidOperationException("Database is closed.");
            IntPtr statement; byte[] query = Native.Utf8(CommandText);
            int result = Native.sqlite3_prepare_v2(connection.Handle, query, query.Length, out statement, IntPtr.Zero);
            if (result != 0 || statement == IntPtr.Zero)
            {
                if (statement != IntPtr.Zero) Native.sqlite3_finalize(statement);
                throw new InvalidDataException(Native.Text(Native.sqlite3_errmsg(connection.Handle)));
            }
            return new SqliteReader(statement, connection);
        }
        public void Dispose() { }
        public int CommandTimeout { get; set; }
        public CommandType CommandType { get; set; } = CommandType.Text;
        public UpdateRowSource UpdatedRowSource { get; set; }
        public IDbConnection Connection { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public IDbTransaction Transaction { get { return null; } set { throw new NotSupportedException(); } }
        public IDataParameterCollection Parameters { get { throw new NotSupportedException(); } }
        public void Cancel() { throw new NotSupportedException(); }
        public IDbDataParameter CreateParameter() { throw new NotSupportedException(); }
        public int ExecuteNonQuery() { throw new NotSupportedException("Card database provider is read-only."); }
        public object ExecuteScalar() { using (var r = ExecuteReader()) return r.Read() ? r.GetValue(0) : null; }
        public void Prepare() { }
    }

    internal sealed class SqliteReader : IDataReader
    {
        private IntPtr statement;
        private readonly SqliteConnection connection;
        private bool done;
        internal SqliteReader(IntPtr statement, SqliteConnection connection) { this.statement = statement; this.connection = connection; }
        public bool Read()
        {
            if (IsClosed) throw new ObjectDisposedException(nameof(SqliteReader));
            if (done) return false;
            int result = Native.sqlite3_step(statement);
            if (result == 100) return true;
            done = true;
            if (result == 101) return false;
            throw new InvalidDataException(Native.Text(Native.sqlite3_errmsg(connection.Handle)));
        }
        public bool IsClosed { get { return statement == IntPtr.Zero; } }
        public void Close() { if (!IsClosed) { Native.sqlite3_finalize(statement); statement = IntPtr.Zero; } }
        public void Dispose() { Close(); }
        public int FieldCount { get { return Native.sqlite3_column_count(statement); } }
        public string GetName(int i) { return Native.Text(Native.sqlite3_column_name(statement, i)); }
        public int GetOrdinal(string name) { for (int i = 0; i < FieldCount; i++) if (string.Equals(GetName(i), name, StringComparison.OrdinalIgnoreCase)) return i; throw new IndexOutOfRangeException(name); }
        public bool IsDBNull(int i) { return Native.sqlite3_column_type(statement, i) == 5; }
        public long GetInt64(int i) { return Native.sqlite3_column_int64(statement, i); }
        public int GetInt32(int i) { return unchecked((int)GetInt64(i)); }
        public short GetInt16(int i) { return (short)GetInt64(i); }
        public string GetString(int i) { return Native.Text(Native.sqlite3_column_text(statement, i), Native.sqlite3_column_bytes(statement, i)); }
        public double GetDouble(int i) { return Native.sqlite3_column_double(statement, i); }
        public float GetFloat(int i) { return (float)GetDouble(i); }
        public decimal GetDecimal(int i) { return (decimal)GetDouble(i); }
        public bool GetBoolean(int i) { return GetInt64(i) != 0; }
        public byte GetByte(int i) { return (byte)GetInt64(i); }
        public char GetChar(int i) { return GetString(i)[0]; }
        public object GetValue(int i)
        {
            switch (Native.sqlite3_column_type(statement, i))
            {
                case 1: return GetInt64(i);
                case 2: return GetDouble(i);
                case 3: return GetString(i);
                case 4:
                    var bytes = new byte[Native.sqlite3_column_bytes(statement, i)];
                    if (bytes.Length > 0) Marshal.Copy(Native.sqlite3_column_blob(statement, i), bytes, 0, bytes.Length);
                    return bytes;
                default: return DBNull.Value;
            }
        }
        public object this[int i] { get { return GetValue(i); } }
        public object this[string name] { get { return GetValue(GetOrdinal(name)); } }
        public int GetValues(object[] values) { int length = Math.Min(values.Length, FieldCount); for (int i = 0; i < length; i++) values[i] = GetValue(i); return length; }
        public Type GetFieldType(int i) { return GetValue(i).GetType(); }
        public string GetDataTypeName(int i) { return GetFieldType(i).Name; }
        public bool NextResult() { return false; }
        public int Depth { get { return 0; } }
        public int RecordsAffected { get { return -1; } }
        public DataTable GetSchemaTable() { throw new NotSupportedException(); }
        public IDataReader GetData(int i) { throw new NotSupportedException(); }
        public Guid GetGuid(int i) { return Guid.Parse(GetString(i)); }
        public DateTime GetDateTime(int i) { return DateTime.Parse(GetString(i), System.Globalization.CultureInfo.InvariantCulture); }
        public long GetBytes(int i, long offset, byte[] buffer, int index, int length) { throw new NotSupportedException(); }
        public long GetChars(int i, long offset, char[] buffer, int index, int length) { throw new NotSupportedException(); }
    }
}
