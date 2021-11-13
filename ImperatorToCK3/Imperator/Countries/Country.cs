﻿using commonItems;
using ImperatorToCK3.Imperator.Characters;
using System.Collections.Generic;

namespace ImperatorToCK3.Imperator.Countries {
	public enum CountryType { rebels, pirates, barbarians, mercenaries, real }
	public enum CountryRank { migrantHorde, cityState, localPower, regionalPower, majorPower, greatPower }
	public enum GovernmentType { monarchy, republic, tribal }
	public partial class Country {
		public ulong Id { get; } = 0;
		public bool PlayerCountry { get; set; }
		private ulong? monarchId;  // >=0 are valid
		public Character? Monarch { get; private set; }
		public List<RulerTerm> RulerTerms { get; set; } = new();
		public Dictionary<string, int> HistoricalRegnalNumbers { get; private set; } = new();
		public string Tag { get; private set; } = "";
		public string Name => CountryName.Name;
		public CountryName CountryName { get; private set; } = new();
		public string Flag { get; private set; } = "";
		public CountryType CountryType { get; private set; } = CountryType.real;
		public ulong? Capital { get; private set; }
		public string? Government { get; private set; }
		public GovernmentType GovernmentType { get; private set; } = GovernmentType.monarchy;
		private readonly SortedSet<string> monarchyLaws = new();
		private readonly SortedSet<string> republicLaws = new();
		private readonly SortedSet<string> tribalLaws = new();
		public Color? Color1 { get; private set; }
		public Color? Color2 { get; private set; }
		public Color? Color3 { get; private set; }
		public CountryCurrencies Currencies { get; private set; } = new();
		private readonly HashSet<ulong> parsedFamilyIds = new();
		public Dictionary<ulong, Families.Family> Families { get; private set; } = new();
		private readonly HashSet<Provinces.Province> ownedProvinces = new();

		public CK3.Titles.Title? CK3Title { get; set; }

		public Country(ulong id) {
			Id = id;
		}
		public SortedSet<string> GetLaws() {
			return GovernmentType switch {
				GovernmentType.monarchy => monarchyLaws,
				GovernmentType.republic => republicLaws,
				GovernmentType.tribal => tribalLaws,
				_ => monarchyLaws,
			};
		}
		public CountryRank GetCountryRank() {
			var provCount = ownedProvinces.Count;
			if (provCount == 0) {
				return CountryRank.migrantHorde;
			}
			if (provCount == 1) {
				return CountryRank.cityState;
			}
			if (provCount <= 24) {
				return CountryRank.localPower;
			}
			if (provCount <= 99) {
				return CountryRank.regionalPower;
			}
			if (provCount <= 499) {
				return CountryRank.majorPower;
			}
			return CountryRank.greatPower;
		}
		public void RegisterProvince(Provinces.Province? province) {
			if (province is null) {
				Logger.Warn($"Didn't register null province to country {Name}.");
			} else {
				ownedProvinces.Add(province);
			}
		}

		// Returns counter of families linked to the country
		public int LinkFamilies(Families.Families families, SortedSet<ulong> idsWithoutDefinition) {
			var counter = 0;
			foreach (var familyId in parsedFamilyIds) {
				if (families.StoredFamilies.TryGetValue(familyId, out var familyToLink)) {
					Families.Add(familyId, familyToLink);
					++counter;
				} else {
					idsWithoutDefinition.Add(familyId);
				}
			}

			return counter;
		}

		public void TryLinkMonarch(Character character) {
			if (monarchId == character.Id) {
				Monarch = character;
			}
		}
	}
}
