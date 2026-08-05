using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ValoCase.Core
{
    /// <summary>One catalog entry: the ISO code and the name shown next to it.</summary>
    public readonly struct Country
    {
        /// <summary>ISO-3166-1 alpha-2, upper case. This is the only value ever sent to the backend.</summary>
        public readonly string Code;

        /// <summary>Display name. Localized only where the country asked for it (TR is Türkiye).</summary>
        public readonly string Name;

        internal Country(string code, string name)
        {
            Code = code;
            Name = name;
        }

        /// <summary>The one display format used everywhere: "Türkiye - TR".</summary>
        public string Label => Name + " - " + Code;
    }

    /// <summary>
    /// The 249 officially assigned ISO-3166-1 alpha-2 codes and the name shown for each.
    ///
    /// One list, one label format, one search. The picker, the profile row and the
    /// registration payload all read from here, so a country can never be displayed one
    /// way in setup and another way in settings, and the name can never reach the wire —
    /// <see cref="Country.Code"/> is the only value that travels.
    ///
    /// Search folds diacritics before matching, so "tur" finds Türkiye and "cote" finds
    /// Côte d'Ivoire. Without that the accented names would be unreachable from a plain
    /// keyboard, which is most of the point of a search box.
    /// </summary>
    public static class CountryCatalog
    {
        // Alphabetical by name, which is also the order the picker shows when the search
        // box is empty. Ranking inside a search bucket inherits this order.
        static readonly Country[] Entries =
        {
            new Country("AF", "Afghanistan"),
            new Country("AX", "Åland Islands"),
            new Country("AL", "Albania"),
            new Country("DZ", "Algeria"),
            new Country("AS", "American Samoa"),
            new Country("AD", "Andorra"),
            new Country("AO", "Angola"),
            new Country("AI", "Anguilla"),
            new Country("AQ", "Antarctica"),
            new Country("AG", "Antigua and Barbuda"),
            new Country("AR", "Argentina"),
            new Country("AM", "Armenia"),
            new Country("AW", "Aruba"),
            new Country("AU", "Australia"),
            new Country("AT", "Austria"),
            new Country("AZ", "Azerbaijan"),
            new Country("BS", "Bahamas"),
            new Country("BH", "Bahrain"),
            new Country("BD", "Bangladesh"),
            new Country("BB", "Barbados"),
            new Country("BY", "Belarus"),
            new Country("BE", "Belgium"),
            new Country("BZ", "Belize"),
            new Country("BJ", "Benin"),
            new Country("BM", "Bermuda"),
            new Country("BT", "Bhutan"),
            new Country("BO", "Bolivia"),
            new Country("BQ", "Bonaire, Sint Eustatius and Saba"),
            new Country("BA", "Bosnia and Herzegovina"),
            new Country("BW", "Botswana"),
            new Country("BV", "Bouvet Island"),
            new Country("BR", "Brazil"),
            new Country("IO", "British Indian Ocean Territory"),
            new Country("BN", "Brunei Darussalam"),
            new Country("BG", "Bulgaria"),
            new Country("BF", "Burkina Faso"),
            new Country("BI", "Burundi"),
            new Country("CV", "Cabo Verde"),
            new Country("KH", "Cambodia"),
            new Country("CM", "Cameroon"),
            new Country("CA", "Canada"),
            new Country("KY", "Cayman Islands"),
            new Country("CF", "Central African Republic"),
            new Country("TD", "Chad"),
            new Country("CL", "Chile"),
            new Country("CN", "China"),
            new Country("CX", "Christmas Island"),
            new Country("CC", "Cocos (Keeling) Islands"),
            new Country("CO", "Colombia"),
            new Country("KM", "Comoros"),
            new Country("CD", "Congo (Democratic Republic of the)"),
            new Country("CG", "Congo"),
            new Country("CK", "Cook Islands"),
            new Country("CR", "Costa Rica"),
            new Country("CI", "Côte d'Ivoire"),
            new Country("HR", "Croatia"),
            new Country("CU", "Cuba"),
            new Country("CW", "Curaçao"),
            new Country("CY", "Cyprus"),
            new Country("CZ", "Czechia"),
            new Country("DK", "Denmark"),
            new Country("DJ", "Djibouti"),
            new Country("DM", "Dominica"),
            new Country("DO", "Dominican Republic"),
            new Country("EC", "Ecuador"),
            new Country("EG", "Egypt"),
            new Country("SV", "El Salvador"),
            new Country("GQ", "Equatorial Guinea"),
            new Country("ER", "Eritrea"),
            new Country("EE", "Estonia"),
            new Country("SZ", "Eswatini"),
            new Country("ET", "Ethiopia"),
            new Country("FK", "Falkland Islands"),
            new Country("FO", "Faroe Islands"),
            new Country("FJ", "Fiji"),
            new Country("FI", "Finland"),
            new Country("FR", "France"),
            new Country("GF", "French Guiana"),
            new Country("PF", "French Polynesia"),
            new Country("TF", "French Southern Territories"),
            new Country("GA", "Gabon"),
            new Country("GM", "Gambia"),
            new Country("GE", "Georgia"),
            new Country("DE", "Germany"),
            new Country("GH", "Ghana"),
            new Country("GI", "Gibraltar"),
            new Country("GR", "Greece"),
            new Country("GL", "Greenland"),
            new Country("GD", "Grenada"),
            new Country("GP", "Guadeloupe"),
            new Country("GU", "Guam"),
            new Country("GT", "Guatemala"),
            new Country("GG", "Guernsey"),
            new Country("GN", "Guinea"),
            new Country("GW", "Guinea-Bissau"),
            new Country("GY", "Guyana"),
            new Country("HT", "Haiti"),
            new Country("HM", "Heard Island and McDonald Islands"),
            new Country("VA", "Holy See"),
            new Country("HN", "Honduras"),
            new Country("HK", "Hong Kong"),
            new Country("HU", "Hungary"),
            new Country("IS", "Iceland"),
            new Country("IN", "India"),
            new Country("ID", "Indonesia"),
            new Country("IR", "Iran"),
            new Country("IQ", "Iraq"),
            new Country("IE", "Ireland"),
            new Country("IM", "Isle of Man"),
            new Country("IL", "Israel"),
            new Country("IT", "Italy"),
            new Country("JM", "Jamaica"),
            new Country("JP", "Japan"),
            new Country("JE", "Jersey"),
            new Country("JO", "Jordan"),
            new Country("KZ", "Kazakhstan"),
            new Country("KE", "Kenya"),
            new Country("KI", "Kiribati"),
            new Country("KP", "Korea (Democratic People's Republic of)"),
            new Country("KR", "Korea (Republic of)"),
            new Country("KW", "Kuwait"),
            new Country("KG", "Kyrgyzstan"),
            new Country("LA", "Lao People's Democratic Republic"),
            new Country("LV", "Latvia"),
            new Country("LB", "Lebanon"),
            new Country("LS", "Lesotho"),
            new Country("LR", "Liberia"),
            new Country("LY", "Libya"),
            new Country("LI", "Liechtenstein"),
            new Country("LT", "Lithuania"),
            new Country("LU", "Luxembourg"),
            new Country("MO", "Macao"),
            new Country("MG", "Madagascar"),
            new Country("MW", "Malawi"),
            new Country("MY", "Malaysia"),
            new Country("MV", "Maldives"),
            new Country("ML", "Mali"),
            new Country("MT", "Malta"),
            new Country("MH", "Marshall Islands"),
            new Country("MQ", "Martinique"),
            new Country("MR", "Mauritania"),
            new Country("MU", "Mauritius"),
            new Country("YT", "Mayotte"),
            new Country("MX", "Mexico"),
            new Country("FM", "Micronesia"),
            new Country("MD", "Moldova"),
            new Country("MC", "Monaco"),
            new Country("MN", "Mongolia"),
            new Country("ME", "Montenegro"),
            new Country("MS", "Montserrat"),
            new Country("MA", "Morocco"),
            new Country("MZ", "Mozambique"),
            new Country("MM", "Myanmar"),
            new Country("NA", "Namibia"),
            new Country("NR", "Nauru"),
            new Country("NP", "Nepal"),
            new Country("NL", "Netherlands"),
            new Country("NC", "New Caledonia"),
            new Country("NZ", "New Zealand"),
            new Country("NI", "Nicaragua"),
            new Country("NE", "Niger"),
            new Country("NG", "Nigeria"),
            new Country("NU", "Niue"),
            new Country("NF", "Norfolk Island"),
            new Country("MK", "North Macedonia"),
            new Country("MP", "Northern Mariana Islands"),
            new Country("NO", "Norway"),
            new Country("OM", "Oman"),
            new Country("PK", "Pakistan"),
            new Country("PW", "Palau"),
            new Country("PS", "Palestine, State of"),
            new Country("PA", "Panama"),
            new Country("PG", "Papua New Guinea"),
            new Country("PY", "Paraguay"),
            new Country("PE", "Peru"),
            new Country("PH", "Philippines"),
            new Country("PN", "Pitcairn"),
            new Country("PL", "Poland"),
            new Country("PT", "Portugal"),
            new Country("PR", "Puerto Rico"),
            new Country("QA", "Qatar"),
            new Country("RE", "Réunion"),
            new Country("RO", "Romania"),
            new Country("RU", "Russian Federation"),
            new Country("RW", "Rwanda"),
            new Country("BL", "Saint Barthélemy"),
            new Country("SH", "Saint Helena, Ascension and Tristan da Cunha"),
            new Country("KN", "Saint Kitts and Nevis"),
            new Country("LC", "Saint Lucia"),
            new Country("MF", "Saint Martin (French part)"),
            new Country("PM", "Saint Pierre and Miquelon"),
            new Country("VC", "Saint Vincent and the Grenadines"),
            new Country("WS", "Samoa"),
            new Country("SM", "San Marino"),
            new Country("ST", "Sao Tome and Principe"),
            new Country("SA", "Saudi Arabia"),
            new Country("SN", "Senegal"),
            new Country("RS", "Serbia"),
            new Country("SC", "Seychelles"),
            new Country("SL", "Sierra Leone"),
            new Country("SG", "Singapore"),
            new Country("SX", "Sint Maarten (Dutch part)"),
            new Country("SK", "Slovakia"),
            new Country("SI", "Slovenia"),
            new Country("SB", "Solomon Islands"),
            new Country("SO", "Somalia"),
            new Country("ZA", "South Africa"),
            new Country("GS", "South Georgia and the South Sandwich Islands"),
            new Country("SS", "South Sudan"),
            new Country("ES", "Spain"),
            new Country("LK", "Sri Lanka"),
            new Country("SD", "Sudan"),
            new Country("SR", "Suriname"),
            new Country("SJ", "Svalbard and Jan Mayen"),
            new Country("SE", "Sweden"),
            new Country("CH", "Switzerland"),
            new Country("SY", "Syrian Arab Republic"),
            new Country("TW", "Taiwan"),
            new Country("TJ", "Tajikistan"),
            new Country("TZ", "Tanzania"),
            new Country("TH", "Thailand"),
            new Country("TL", "Timor-Leste"),
            new Country("TG", "Togo"),
            new Country("TK", "Tokelau"),
            new Country("TO", "Tonga"),
            new Country("TT", "Trinidad and Tobago"),
            new Country("TN", "Tunisia"),
            new Country("TR", "Türkiye"),
            new Country("TM", "Turkmenistan"),
            new Country("TC", "Turks and Caicos Islands"),
            new Country("TV", "Tuvalu"),
            new Country("UG", "Uganda"),
            new Country("UA", "Ukraine"),
            new Country("AE", "United Arab Emirates"),
            new Country("GB", "United Kingdom"),
            new Country("US", "United States"),
            new Country("UM", "United States Minor Outlying Islands"),
            new Country("UY", "Uruguay"),
            new Country("UZ", "Uzbekistan"),
            new Country("VU", "Vanuatu"),
            new Country("VE", "Venezuela"),
            new Country("VN", "Viet Nam"),
            new Country("VG", "Virgin Islands (British)"),
            new Country("VI", "Virgin Islands (U.S.)"),
            new Country("WF", "Wallis and Futuna"),
            new Country("EH", "Western Sahara"),
            new Country("YE", "Yemen"),
            new Country("ZM", "Zambia"),
            new Country("ZW", "Zimbabwe"),
        };

        // Folded names, built once, in the same order as Entries. Folding on every
        // keystroke instead would re-normalise 249 strings per character typed.
        static readonly string[] FoldedNames = BuildFoldedNames();

        static readonly Dictionary<string, int> IndexByCode = BuildIndex();

        // Match quality buckets, reused across searches so filtering allocates nothing
        // after the first call. 0: exact code, 1: code prefix, 2: name prefix, 3: name contains.
        static readonly List<Country>[] Buckets =
        {
            new List<Country>(), new List<Country>(), new List<Country>(), new List<Country>()
        };

        /// <summary>Every country, alphabetical by name.</summary>
        public static IReadOnlyList<Country> All => Entries;

        /// <summary>Whether the value is an officially assigned alpha-2 code (case-insensitive).</summary>
        public static bool IsValidCode(string code) => !string.IsNullOrEmpty(Normalize(code));

        /// <summary>
        /// The canonical upper-case code, or empty when the value is not in the catalog.
        /// Everything that stores or sends a country goes through this, so a stale or
        /// hand-edited save cannot put an unassigned code on the wire.
        /// </summary>
        public static string Normalize(string code)
        {
            if (string.IsNullOrEmpty(code)) return string.Empty;
            var trimmed = code.Trim().ToUpperInvariant();
            return IndexByCode.ContainsKey(trimmed) ? trimmed : string.Empty;
        }

        public static bool TryGet(string code, out Country country)
        {
            var normalized = Normalize(code);
            if (normalized.Length == 0) { country = default; return false; }
            country = Entries[IndexByCode[normalized]];
            return true;
        }

        /// <summary>
        /// "Türkiye - TR" for a known code, empty for anything else. Empty rather than the
        /// raw code on purpose: a code the catalog does not know is not a country the
        /// player picked here, and showing it would look like a working selection.
        /// </summary>
        public static string LabelFor(string code) => TryGet(code, out var country) ? country.Label : string.Empty;

        /// <summary>
        /// Fills <paramref name="results"/> with the countries matching <paramref name="query"/>,
        /// best match first. An empty query returns the whole catalog. Matching is
        /// case-insensitive and diacritic-insensitive, against both the name and the code.
        /// </summary>
        public static void Search(string query, List<Country> results)
        {
            if (results == null) return;
            results.Clear();

            var folded = Fold(query);
            if (folded.Length == 0)
            {
                results.AddRange(Entries);
                return;
            }

            for (int i = 0; i < Buckets.Length; i++) Buckets[i].Clear();

            for (int i = 0; i < Entries.Length; i++)
            {
                int rank = Rank(Entries[i].Code, FoldedNames[i], folded);
                if (rank >= 0) Buckets[rank].Add(Entries[i]);
            }

            for (int i = 0; i < Buckets.Length; i++) results.AddRange(Buckets[i]);
        }

        // Each country lands in exactly one bucket — the best one it qualifies for — so
        // the concatenated result never repeats an entry.
        static int Rank(string code, string foldedName, string foldedQuery)
        {
            if (foldedQuery.Length <= 2)
            {
                var foldedCode = code.ToLowerInvariant();
                if (string.Equals(foldedCode, foldedQuery, StringComparison.Ordinal)) return 0;
                if (foldedCode.StartsWith(foldedQuery, StringComparison.Ordinal)) return 1;
            }

            if (foldedName.StartsWith(foldedQuery, StringComparison.Ordinal)) return 2;
            if (foldedName.IndexOf(foldedQuery, StringComparison.Ordinal) >= 0) return 3;
            return -1;
        }

        /// <summary>
        /// Lower case with combining marks removed, so "Türkiye" and "Turkiye" are the
        /// same string to the search. Decomposing first is what makes that work: NFD
        /// splits ü into u + a combining diaeresis, and the mark is then dropped.
        /// </summary>
        static string Fold(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var trimmed = value.Trim();
            if (trimmed.Length == 0) return string.Empty;

            string decomposed;
            try
            {
                decomposed = trimmed.Normalize(NormalizationForm.FormD);
            }
            catch (ArgumentException)
            {
                // Unpaired surrogate — nothing in the catalog can match it anyway, so the
                // raw text is carried forward rather than throwing out of a keystroke.
                decomposed = trimmed;
            }

            var sb = new StringBuilder(decomposed.Length);
            foreach (var ch in decomposed)
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }

        static string[] BuildFoldedNames()
        {
            var folded = new string[Entries.Length];
            for (int i = 0; i < Entries.Length; i++) folded[i] = Fold(Entries[i].Name);
            return folded;
        }

        static Dictionary<string, int> BuildIndex()
        {
            var index = new Dictionary<string, int>(Entries.Length, StringComparer.Ordinal);
            for (int i = 0; i < Entries.Length; i++) index[Entries[i].Code] = i;
            return index;
        }
    }
}
