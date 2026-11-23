# Erzeugt Main.xml mit Motorsteuerung (Vor/Rueck, Selbsthaltung, Stop, Nothalt, Endlagen)
# Aufruf: .\gen_main.ps1 -Path <Exportordner>\PLC_1\Programmbausteine\Main.xml
param([Parameter(Mandatory)][string]$Path)
$path = $Path
$xml = Get-Content $path -Raw -Encoding UTF8

function Network($id, $title, $start, $self, $other, $endlage, $motor) {
  $ns = 'http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v4'
  $flg = @"
<FlgNet xmlns="$ns">
<Parts>
<Access Scope="GlobalVariable" UId="21"><Symbol><Component Name="$start" /></Symbol></Access>
<Part Name="Contact" UId="22" />
<Access Scope="GlobalVariable" UId="23"><Symbol><Component Name="$self" /></Symbol></Access>
<Part Name="Contact" UId="24" />
<Part Name="O" UId="25"><TemplateValue Name="Card" Type="Cardinality">2</TemplateValue></Part>
<Access Scope="GlobalVariable" UId="26"><Symbol><Component Name="Taster_Stop" /></Symbol></Access>
<Part Name="Contact" UId="27" />
<Access Scope="GlobalVariable" UId="28"><Symbol><Component Name="Nothalt" /></Symbol></Access>
<Part Name="Contact" UId="29" />
<Access Scope="GlobalVariable" UId="30"><Symbol><Component Name="$endlage" /></Symbol></Access>
<Part Name="Contact" UId="31" />
<Access Scope="GlobalVariable" UId="32"><Symbol><Component Name="$other" /></Symbol></Access>
<Part Name="Contact" UId="33"><Negated Name="operand" /></Part>
<Access Scope="GlobalVariable" UId="34"><Symbol><Component Name="$motor" /></Symbol></Access>
<Part Name="Coil" UId="35" />
</Parts>
<Wires>
<Wire UId="40"><Powerrail /><NameCon UId="22" Name="in" /><NameCon UId="24" Name="in" /></Wire>
<Wire UId="41"><IdentCon UId="21" /><NameCon UId="22" Name="operand" /></Wire>
<Wire UId="42"><IdentCon UId="23" /><NameCon UId="24" Name="operand" /></Wire>
<Wire UId="43"><NameCon UId="22" Name="out" /><NameCon UId="25" Name="in1" /></Wire>
<Wire UId="44"><NameCon UId="24" Name="out" /><NameCon UId="25" Name="in2" /></Wire>
<Wire UId="45"><NameCon UId="25" Name="out" /><NameCon UId="27" Name="in" /></Wire>
<Wire UId="46"><IdentCon UId="26" /><NameCon UId="27" Name="operand" /></Wire>
<Wire UId="47"><NameCon UId="27" Name="out" /><NameCon UId="29" Name="in" /></Wire>
<Wire UId="48"><IdentCon UId="28" /><NameCon UId="29" Name="operand" /></Wire>
<Wire UId="49"><NameCon UId="29" Name="out" /><NameCon UId="31" Name="in" /></Wire>
<Wire UId="50"><IdentCon UId="30" /><NameCon UId="31" Name="operand" /></Wire>
<Wire UId="51"><NameCon UId="31" Name="out" /><NameCon UId="33" Name="in" /></Wire>
<Wire UId="52"><IdentCon UId="32" /><NameCon UId="33" Name="operand" /></Wire>
<Wire UId="53"><NameCon UId="33" Name="out" /><NameCon UId="35" Name="in" /></Wire>
<Wire UId="54"><IdentCon UId="34" /><NameCon UId="35" Name="operand" /></Wire>
</Wires>
</FlgNet>
"@
  $b = $id
  return @"
      <SW.Blocks.CompileUnit ID="$b" CompositionName="CompileUnits">
        <AttributeList>
          <NetworkSource>$flg</NetworkSource>
          <ProgrammingLanguage>LAD</ProgrammingLanguage>
        </AttributeList>
        <ObjectList>
          <MultilingualText ID="$($b+1)" CompositionName="Comment">
            <ObjectList>
              <MultilingualTextItem ID="$($b+2)" CompositionName="Items">
                <AttributeList><Culture>de-DE</Culture><Text /></AttributeList>
              </MultilingualTextItem>
            </ObjectList>
          </MultilingualText>
          <MultilingualText ID="$($b+3)" CompositionName="Title">
            <ObjectList>
              <MultilingualTextItem ID="$($b+4)" CompositionName="Items">
                <AttributeList><Culture>de-DE</Culture><Text>$title</Text></AttributeList>
              </MultilingualTextItem>
            </ObjectList>
          </MultilingualText>
        </ObjectList>
      </SW.Blocks.CompileUnit>
"@
}

$n1 = Network 100 'Motor Vorwaerts' 'Taster_Start_Vor' 'Motor_Vor' 'Motor_Rueck' 'Endlage_Vor' 'Motor_Vor'
$n2 = Network 200 'Motor Rueckwaerts' 'Taster_Start_Rueck' 'Motor_Rueck' 'Motor_Vor' 'Endlage_Rueck' 'Motor_Rueck'

# vorhandene leere CompileUnit (ID 3) ersetzen
$pattern = '(?s)\s*<SW\.Blocks\.CompileUnit ID="3".*?</SW\.Blocks\.CompileUnit>'
if ($xml -notmatch $pattern) { throw 'CompileUnit ID=3 nicht gefunden (Main.xml schon veraendert?)' }
$xml = [regex]::Replace($xml, $pattern, "`r`n$n1`r`n$n2")
[IO.File]::WriteAllText($path, $xml, (New-Object Text.UTF8Encoding($false)))
"OK: $path"
