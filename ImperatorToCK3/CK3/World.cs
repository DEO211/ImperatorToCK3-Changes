﻿using commonItems;
using ImperatorToCK3.CK3.Characters;
using ImperatorToCK3.CK3.Dynasties;
using ImperatorToCK3.CK3.Map;
using ImperatorToCK3.CK3.Provinces;
using ImperatorToCK3.CK3.Titles;
using ImperatorToCK3.Imperator.Countries;
using ImperatorToCK3.Imperator.Jobs;
using ImperatorToCK3.Mappers.CoA;
using ImperatorToCK3.Mappers.Culture;
using ImperatorToCK3.Mappers.DeathReason;
using ImperatorToCK3.Mappers.Government;
using ImperatorToCK3.Mappers.Localization;
using ImperatorToCK3.Mappers.Nickname;
using ImperatorToCK3.Mappers.Province;
using ImperatorToCK3.Mappers.Region;
using ImperatorToCK3.Mappers.Religion;
using ImperatorToCK3.Mappers.SuccessionLaw;
using ImperatorToCK3.Mappers.TagTitle;
using ImperatorToCK3.Mappers.Trait;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ImperatorToCK3.CK3 {
	public class World {
		public CharacterCollection Characters { get; } = new();
		public DynastyCollection Dynasties { get; } = new();
		public ProvinceCollection Provinces { get; } = new();
		public Title.LandedTitles LandedTitles { get; } = new();
		public MapData MapData { get; }
		public Date CorrectedDate { get; }

		public World(Imperator.World impWorld, Configuration config) {
			Logger.Info("*** Hello CK3, let's get painting. ***");
			CorrectedDate = impWorld.EndDate.Year > 0 ? impWorld.EndDate : new Date(1, 1, 1);

			Logger.Info("Loading map data...");
			MapData = new MapData(config.CK3Path);

			// Scraping localizations from Imperator so we may know proper names for our countries.
			localizationMapper.ScrapeLocalizations(config, impWorld.Mods);

			// Loading Imperator CoAs to use them for generated CK3 titles
			coaMapper = new CoaMapper(config, impWorld.Mods);

			// Load vanilla titles history
			var titlesHistoryPath = Path.Combine(config.CK3Path, "game/history/titles");
			titlesHistory = new TitlesHistory(titlesHistoryPath);

			// Load vanilla CK3 landed titles
			var landedTitlesPath = Path.Combine(config.CK3Path, "game/common/landed_titles/00_landed_titles.txt");

			LandedTitles.LoadTitles(landedTitlesPath);
			AddHistoryToVanillaTitles(config.CK3BookmarkDate);

			// Loading regions
			ck3RegionMapper = new CK3RegionMapper(config.CK3Path, LandedTitles);
			imperatorRegionMapper = new ImperatorRegionMapper(config.ImperatorPath, impWorld.Mods);
			// Use the region mappers in other mappers
			religionMapper.LoadRegionMappers(imperatorRegionMapper, ck3RegionMapper);
			cultureMapper.LoadRegionMappers(imperatorRegionMapper, ck3RegionMapper);

			LandedTitles.ImportImperatorCountries(
				impWorld.Countries,
				tagTitleMapper,
				localizationMapper,
				provinceMapper,
				coaMapper,
				governmentMapper,
				successionLawMapper,
				definiteFormMapper,
				religionMapper,
				cultureMapper,
				nicknameMapper,
				Characters,
				CorrectedDate
			);
			LandedTitles.ImportImperatorGovernorships(
				impWorld,
				tagTitleMapper,
				localizationMapper,
				provinceMapper,
				definiteFormMapper,
				imperatorRegionMapper,
				coaMapper
			);

			// Now we can deal with provinces since we know to whom to assign them. We first import vanilla province data.
			// Some of it will be overwritten, but not all.
			Provinces.ImportVanillaProvinces(config.CK3Path, config.CK3BookmarkDate);

			// Next we import Imperator provinces and translate them ontop a significant part of all imported provinces.
			Provinces.ImportImperatorProvinces(impWorld, LandedTitles, cultureMapper, religionMapper, provinceMapper);

			Characters.ImportImperatorCharacters(
				impWorld,
				religionMapper,
				cultureMapper,
				traitMapper,
				nicknameMapper,
				localizationMapper,
				provinceMapper,
				deathReasonMapper,
				CorrectedDate,
				config.CK3BookmarkDate
			);
			ClearFeaturedCharactersDescriptions(config.CK3BookmarkDate);

			Dynasties.ImportImperatorFamilies(impWorld, localizationMapper);

			OverWriteCountiesHistory(impWorld.Jobs.Governorships, CorrectedDate);
			LandedTitles.RemoveInvalidLandlessTitles(config.CK3BookmarkDate);

			Characters.PurgeLandlessVanillaCharacters(LandedTitles, config.CK3BookmarkDate);
			Characters.RemoveEmployerIdFromLandedCharacters(LandedTitles, CorrectedDate);
		}

		private void ClearFeaturedCharactersDescriptions(Date ck3BookmarkDate) {
			Logger.Info("Clearing featured characters' descriptions.");
			foreach (var title in LandedTitles) {
				if (!title.PlayerCountry) {
					continue;
				}
				var holderId = title.GetHolderId(ck3BookmarkDate);
				if (holderId != "0" && Characters.TryGetValue(holderId, out var holder)) {
					title.Localizations.Add($"{holder.Name}_desc", new LocBlock());
				}
			}
		}

		private void AddHistoryToVanillaTitles(Date ck3BookmarkDate) {
			foreach (var title in LandedTitles) {
				var historyOpt = titlesHistory.PopTitleHistory(title.Id);
				if (historyOpt is not null) {
					title.AddHistory(historyOpt);
				}
			}
			// Add vanilla development to counties
			// For counties that inherit development level from de jure lieges, assign it to them directly for better reliability
			foreach (Title title in LandedTitles.Where(title => title.Rank == TitleRank.county && title.GetDevelopmentLevel(ck3BookmarkDate) is null)) {
				var inheritedDev = title.GetOwnOrInheritedDevelopmentLevel(ck3BookmarkDate);
				title.SetDevelopmentLevel(inheritedDev ?? 0, ck3BookmarkDate);
			}

			// remove history entries past the bookmark date
			foreach (var title in LandedTitles) {
				title.RemoveHistoryPastBookmarkDate(ck3BookmarkDate);
			}
		}

		private void OverWriteCountiesHistory(IReadOnlyCollection<Governorship> governorships, Date conversionDate) {
			Logger.Info("Overwriting counties' history...");
			foreach (var county in LandedTitles.Where(t => t.Rank == TitleRank.county)) {
				ulong capitalBaronyProvinceId = (ulong)county.CapitalBaronyProvince!;
				if (capitalBaronyProvinceId == 0) {
					// title's capital province has an invalid ID (0 is not a valid province in CK3)
					continue;
				}

				if (!Provinces.ContainsKey(capitalBaronyProvinceId)) {
					Logger.Warn($"Capital barony province not found: {county.CapitalBaronyProvince}");
					continue;
				}

				var ck3CapitalBaronyProvince = Provinces[capitalBaronyProvinceId];
				var impProvince = ck3CapitalBaronyProvince.ImperatorProvince;
				if (impProvince is null) { // probably outside of Imperator map
					continue;
				}

				var impCountry = impProvince.OwnerCountry;

				if (impCountry is null || impCountry.CountryType == CountryType.rebels) { // e.g. uncolonized Imperator province
					county.SetHolderId("0", conversionDate);
					county.SetDeFactoLiege(null, conversionDate);
				} else {
					var ck3Country = impCountry.CK3Title;
					if (ck3Country is null) {
						Logger.Warn($"{impCountry.Name} has no CK3 title!"); // should not happen
						continue;
					}
					var ck3CapitalCounty = ck3Country.CapitalCounty;
					var impMonarch = impCountry.Monarch;
					var matchingGovernorships = new List<Governorship>(governorships.Where(g =>
						g.CountryId == impCountry.Id &&
						g.RegionName == imperatorRegionMapper.GetParentRegionName(impProvince.Id)
					));

					if (ck3CapitalCounty is null) {
						if (impMonarch is not null) {
							GiveCountyToMonarch(county, ck3Country, impMonarch.Id);
						} else {
							Logger.Warn($"Imperator ruler doesn't exist for {impCountry.Name} owning {county.Id}!");
						}
						continue;
					}
					// if title belongs to country ruler's capital's de jure duchy, make it directly held by the ruler
					var countryCapitalDuchy = ck3CapitalCounty.DeJureLiege;
					var titleLiegeDuchy = county.DeJureLiege;
					if (countryCapitalDuchy is not null && titleLiegeDuchy is not null && countryCapitalDuchy.Id == titleLiegeDuchy.Id) {
						if (impMonarch is not null) {
							GiveCountyToMonarch(county, ck3Country, impMonarch.Id);
						}
					} else if (matchingGovernorships.Count > 0) {
						// give county to governor
						var governorship = matchingGovernorships[0];
						var ck3GovernorshipName = tagTitleMapper.GetTitleForGovernorship(governorship.RegionName, impCountry.Tag, ck3Country.Id);
						if (ck3GovernorshipName is null) {
							Logger.Warn($"{nameof(ck3GovernorshipName)} is null for {ck3Country.Id} {governorship.RegionName}!");
							continue;
						}
						GiveCountyToGovernor(county, ck3GovernorshipName);
					} else if (impMonarch is not null) {
						GiveCountyToMonarch(county, ck3Country, impMonarch.Id);
					}
				}
			}

			void GiveCountyToMonarch(Title county, Title ck3Country, ulong impMonarchId) {
				var holderId = $"imperator{impMonarchId}";
				var date = ck3Country.GetDateOfLastHolderChange();
				if (Characters.TryGetValue(holderId, out var holder)) {
					county.ClearHolderSpecificHistory();
					county.SetHolderId(holder.Id, date);
				} else {
					Logger.Warn($"Holder {holderId} of county {county.Id} doesn't exist!");
				}
				county.SetDeFactoLiege(null, date);
			}

			void GiveCountyToGovernor(Title county, string ck3GovernorshipName) {
				var ck3Governorship = LandedTitles[ck3GovernorshipName];
				var holderChangeDate = ck3Governorship.GetDateOfLastHolderChange();
				var holderId = ck3Governorship.GetHolderId(holderChangeDate);
				if (Characters.TryGetValue(holderId, out var governor)) {
					county.ClearHolderSpecificHistory();
					county.SetHolderId(governor.Id, holderChangeDate);
				} else {
					Logger.Warn($"Holder {holderId} of county {county.Id} doesn't exist!");
				}
				county.SetDeFactoLiege(null, holderChangeDate);
			}
		}

		private readonly CoaMapper coaMapper;
		private readonly CultureMapper cultureMapper = new();
		private readonly DeathReasonMapper deathReasonMapper = new();
		private readonly DefiniteFormMapper definiteFormMapper = new("configurables/definite_form_names.txt");
		private readonly GovernmentMapper governmentMapper = new();
		private readonly LocalizationMapper localizationMapper = new();
		private readonly NicknameMapper nicknameMapper = new("configurables/nickname_map.txt");
		private readonly ProvinceMapper provinceMapper = new();
		private readonly ReligionMapper religionMapper = new();
		private readonly SuccessionLawMapper successionLawMapper = new("configurables/succession_law_map.txt");
		private readonly TagTitleMapper tagTitleMapper = new("configurables/title_map.txt", "configurables/governorMappings.txt");
		private readonly TraitMapper traitMapper = new("configurables/trait_map.txt");
		private readonly CK3RegionMapper ck3RegionMapper;
		private readonly ImperatorRegionMapper imperatorRegionMapper;
		private readonly TitlesHistory titlesHistory;
	}
}
