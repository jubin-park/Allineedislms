using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Allineedislms
{
    class LmsKlassInfo
    {
        public string Name { get; set; } // 과목명
        public uint No { get; set; } // 분반
        public string ProfessorName { get; set; } // 교수
        public uint StudentCount { get; set; } // 수강인원
        public string NewCode { get; set; }
        public uint OldId { get; set; }
        public uint NewId { get; set; }
        public bool IsOldChecked { get; set; }
        public bool IsNewChecked { get; set; }

        // Default Constructor for Json Deserialization
        public LmsKlassInfo()
        {

        }

        public LmsKlassInfo(List<string> data)
        {
            Debug.Assert(data != null);
            Debug.Assert(data.Count == 7);

            uint temp;
            bool isSuccess;

            Name = data[0];

            isSuccess = uint.TryParse(data[1], out temp);
            Debug.Assert(isSuccess);
            No = temp;

            ProfessorName = data[2];

            uint.TryParse(data[3], out temp);
            Debug.Assert(isSuccess);
            StudentCount = temp;

            NewCode = data[4];

            uint.TryParse(data[5], out temp);
            Debug.Assert(isSuccess);
            OldId = temp;

            uint.TryParse(data[6], out temp);
            Debug.Assert(isSuccess);
            NewId = temp;

            IsOldChecked = false;
            IsNewChecked = false;
        }
    }
}
