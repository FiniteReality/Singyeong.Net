using System;
using System.Linq.Expressions;


namespace Singyeong.Protocol
{
    internal struct SingyeongTarget
    {
        public string ApplicationId { get; set; }
        public bool AllowRestricted { get; set; }
        public string ConsistentHashKey { get; set; }
        public Expression<Func<SingyeongQuery, bool>>? Query { get; set; }
    }
}