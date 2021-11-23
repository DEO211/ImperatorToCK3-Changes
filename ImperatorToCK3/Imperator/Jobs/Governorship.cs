﻿using commonItems;

namespace ImperatorToCK3.Imperator.Jobs {
	public class Governorship {
		public ulong CountryId { get; private set; } = 0;
		public ulong CharacterId { get; private set; } = 0;
		public Date StartDate { get; private set; } = new(1, 1, 1);
		public string RegionName { get; private set; } = string.Empty;

		public Governorship(BufferedReader reader) {
			var parser = new Parser();
			parser.RegisterKeyword("who", reader => CountryId = reader.GetULong());
			parser.RegisterKeyword("character", reader => CharacterId = reader.GetULong());
			parser.RegisterKeyword("start_date", reader => {
				var dateStr = reader.GetString();
				StartDate = new Date(dateStr, AUC: true);
			});
			parser.RegisterKeyword("governorship", reader => RegionName = reader.GetString());
			parser.RegisterRegex(CommonRegexes.Catchall, ParserHelpers.IgnoreAndLogItem);

			parser.ParseStream(reader);
		}
	}
}
