$ErrorActionPreference = 'Stop'

$suiteRows = @'
Not the Bees|not-the-bees|notthebees|
Drunk|drunk|drunk|
Celebration Mk10|celebration-mk10|celebration|
The Constant|the-constant|theconstant|
For the Worthy|for-the-worthy|fortheworthy|
No Traps|no-traps|notraps|
Remix / Don't Dig Up|remix|remix|
Zenith / Get Fixed Boi|zenith|zenith|
Skyblock|skyblock|skyblock|
Abandoned Manors|abandoned-manors||Abandoned manors
Arachnophobia|arachnophobia||Arachnophobia
Beam Me Up|beam-me-up||Beam me up
Bring a Towel|bring-a-towel||Bring a towel
Calm Before the Storm|calm-before-the-storm||Calm before the storm
Double Daring Dangers|double-daring-dangers||Double daring dangers
Electric Boogaloo|electric-boogaloo||Electric Boogaloo
Fish Mox|fish-mox||Fish Mox
Hocus Pocus|hocus-pocus||Hocus pocus
How Did I Get Here|how-did-i-get-here||How did I get here
I Am Error|i-am-error||I am error
Invisible Plane|invisible-plane||Invisible plane
Jagged Rocks|jagged-rocks||Jagged rocks
Jingle All the Way|jingle-all-the-way||Jingle all the way
Mole People|mole-people||Mole people
Monochrome|monochrome||Monochrome
More Traps Please|more-traps-please||More traps please
Negative Infinity|negative-infinity||Negative infinity
Night of the Living Dead|night-of-the-living-dead||Night of the Living Dead
Planetoids|planetoids||Planetoids
Pumpkin Season|pumpkin-season||Pumpkin season
Purify This|purify-this||Purify this
Rainbow Road|rainbow-road||Rainbow Road
Royale With Cheese|royale-with-cheese||Royale with cheese
Does That Sparkle|does-that-sparkle||Does that sparkle
Too Easy|too-easy||Too easy
Waterpark|waterpark||Waterpark
What a Horrible Night to Have a Curse|what-a-horrible-night||What a horrible night to have a curse
Winter Is Coming|winter-is-coming||Winter is coming
X-Ray Vision|x-ray-vision||X-ray vision
Truck Stop|truck-stop||Truck stop
Sandy Britches|sandy-britches||Sandy britches
Save the Rainforest|save-the-rainforest||Save the rainforest
Such Great Heights|such-great-heights||Such great heights
The Care Bears Movie|care-bears-movie||The Care Bears Movie
Toadstool|toadstool||Toadstool
We Don't Even Test for That|we-dont-even-test-for-that||We don't even test for that
'@ -split "`n"

$suites = foreach ($row in $suiteRows) {
    $row = $row.TrimEnd("`r")
    if ([string]::IsNullOrWhiteSpace($row)) { continue }
    $parts = $row.Split('|')
    if ($parts.Count -ne 4) { throw "Bad suite row: $row" }
    [ordered]@{
        label = $parts[0]
        slug = $parts[1]
        special_config = $parts[2]
        secret_text = $parts[3]
    }
}

$worldRows = @'
Small|small||1|1|4200|1200
Medium|medium||2|2|6400|1800
Large|large||3|3|8400|2400
THICC|thicc|THICC|3|3|10600|3000
THICC 2|thicc2|THICC2|3|3|12600|3600
THICC 3|thicc3|THICC3|3|3|14800|4200
THICC 4|thicc4|THICC4|3|3|16800|4800
THICC 5|thicc5|THICC5|3|3|19000|5400
THICC 6|thicc6|THICC6|3|3|21000|6000
THICC 7|thicc7|THICC7|3|3|23200|6600
THICC 8|thicc8|THICC8|3|3|25200|7200
THICC 9|thicc9|THICC9|3|3|27400|7800
THICC 10|thicc10|THICC10|3|3|29400|8400
THICC 11|thicc11|THICC11|3|3|31600|9000
'@ -split "`n"

$worlds = foreach ($row in $worldRows) {
    $row = $row.TrimEnd("`r")
    if ([string]::IsNullOrWhiteSpace($row)) { continue }
    $parts = $row.Split('|')
    if ($parts.Count -ne 7) { throw "Bad world row: $row" }
    [ordered]@{
        preset = $parts[0]
        world_slug = $parts[1]
        selector = $parts[2]
        copied_size = [int]$parts[3]
        autocreate = [int]$parts[4]
        width = [int]$parts[5]
        height = [int]$parts[6]
    }
}

if ($suites.Count -ne 46) { throw "Expected 46 suites; found $($suites.Count)." }
if ($worlds.Count -ne 14) { throw "Expected 14 world sizes; found $($worlds.Count)." }
if ((@($suites | Where-Object { -not [string]::IsNullOrWhiteSpace($_.special_config) })).Count -ne 9) { throw 'Expected exactly 9 Special Seeds.' }
if ((@($suites | Where-Object { -not [string]::IsNullOrWhiteSpace($_.secret_text) })).Count -ne 37) { throw 'Expected exactly 37 Secret Seeds.' }
if ((@($suites.slug | Sort-Object -Unique)).Count -ne 46) { throw 'Suite slugs must be unique.' }

function New-SizeFirstMatrix([object[]]$selectedWorlds) {
    $include = @()
    foreach ($world in $selectedWorlds) {
        foreach ($suite in $suites) {
            $include += [ordered]@{
                suite_label = $suite.label
                suite_slug = $suite.slug
                special_config = $suite.special_config
                secret_text = $suite.secret_text
                preset = $world.preset
                world_slug = $world.world_slug
                selector = $world.selector
                copied_size = $world.copied_size
                autocreate = $world.autocreate
                width = $world.width
                height = $world.height
            }
        }
    }
    [ordered]@{ include = $include } | ConvertTo-Json -Compress -Depth 6
}

$suiteInclude = foreach ($suite in $suites) {
    [ordered]@{ suite_label = $suite.label; suite_slug = $suite.slug }
}

$batchA = New-SizeFirstMatrix @($worlds[0..4])
$batchB = New-SizeFirstMatrix @($worlds[5..9])
$batchC = New-SizeFirstMatrix @($worlds[10..13])
$suiteMatrix = [ordered]@{ include = @($suiteInclude) } | ConvertTo-Json -Compress -Depth 4

"batch_a=$batchA" >> $env:GITHUB_OUTPUT
"batch_b=$batchB" >> $env:GITHUB_OUTPUT
"batch_c=$batchC" >> $env:GITHUB_OUTPUT
"suites=$suiteMatrix" >> $env:GITHUB_OUTPUT

Write-Host 'Planned 46 comparison families and 644 fresh worlds, ordered by size.'
