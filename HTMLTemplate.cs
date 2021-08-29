using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Allineedislms
{
    class HTMLTemplate
    {
        public static readonly string IEILMS_LOGIN =
@"<html>
<head>
<script language=""Javascript"">
function submitForm()
        {{
            var theForm = document.getElementById(""theForm"");
            theForm.submit();
        }}
</script>
</head>
<body onload=""submitForm()"">
<form id=""theForm"" action=""{0}"" method=""POST"">
    <input type=""hidden"" name=""{1}"" value=""{3}""/>
    <input type=""hidden"" name=""{2}"" value=""{4}""/>
</form>
</body>
</html>";
    }
}
