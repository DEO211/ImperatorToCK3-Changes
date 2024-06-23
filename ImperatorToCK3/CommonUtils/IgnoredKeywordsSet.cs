using System.Collections.Generic;
using System.Linq;

namespace ImperatorToCK3.CommonUtils;

public class IgnoredKeywordsSet : HashSet<string> {
	public override string ToString() {
		return string.Join(", ", this.Order());
	}
}