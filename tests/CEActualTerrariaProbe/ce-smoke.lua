local workspace = os.getenv('GITHUB_WORKSPACE')
if workspace == nil or workspace == '' then
  return
end

local testdir = workspace .. [[\tests\CEActualTerrariaProbe]]
local resultPath = testdir .. [[\ce-result.txt]]
local markerPath = testdir .. [[\terraria-probe.txt]]
local probePayloadPath = testdir .. [[\out\GLoaderCeTerrariaProbe.dll]]
local helperPath = testdir .. [[\out-helper\TerrariaCEHelper.dll]]

local function writeResult(text)
  local f = assert(io.open(resultPath, 'w'))
  f:write(text)
  f:write('\n')
  f:close()
end

local attempts = 0
local done = false
hideAllCEWindows()

local timer = createTimer(nil, false)
timer.Interval = 50
timer.OnTimer = function(t)
  if done then
    t.Enabled = false
    return
  end

  attempts = attempts + 1
  if attempts > 1200 then
    done = true
    writeResult('FAIL timeout waiting to inject into gloader.exe')
    t.Enabled = false
    closeCE()
    return
  end

  local pid = getProcessIDFromProcessName('gloader.exe')
  if pid == nil or pid == 0 then
    return
  end

  local ok, message = pcall(function()
    if injectDotNetDLL == nil then
      dofile(getCheatEngineDir() .. [[autorun\DotNetInject.lua]])
    end
    if injectDotNetDLL == nil then
      error('DotNetInject.lua did not expose injectDotNetDLL')
    end

    openProcess(pid)
    if getOpenedProcessID() ~= pid then
      return
    end

    local function inject(path, className, methodName, parameter)
      local returnValue, injectError = injectDotNetDLL(path, className, methodName, parameter)
      if returnValue == nil then
        if injectError == -4 or injectError == -2 or injectError == -1 then
          return nil, injectError
        end
        error('injectDotNetDLL failed with error ' .. tostring(injectError))
      end
      return returnValue, nil
    end

    local probeReturn, transient = inject(
      probePayloadPath,
      'GLoaderCeTerrariaProbe.EntryPoint',
      'Initialize',
      markerPath)
    if probeReturn == nil and transient ~= nil then return end
    if probeReturn ~= 23063 then
      error('reflection probe returned ' .. tostring(probeReturn) .. ', expected 23063')
    end

    local selfTest = assert(inject(helperPath, 'TerrariaCEHelper.EntryPoint', 'Run', 'selftest'))
    if selfTest ~= 23063 then error('helper selftest returned ' .. tostring(selfTest)) end

    local fishOn = assert(inject(helperPath, 'TerrariaCEHelper.EntryPoint', 'Run', 'fish-on'))
    if fishOn ~= 23063 then error('fish-on returned ' .. tostring(fishOn)) end

    local statusOn = assert(inject(helperPath, 'TerrariaCEHelper.EntryPoint', 'Run', 'fish-status'))
    if statusOn ~= 23063 then error('fish-status after enable returned ' .. tostring(statusOn)) end

    local fishOff = assert(inject(helperPath, 'TerrariaCEHelper.EntryPoint', 'Run', 'fish-off'))
    if fishOff ~= 23063 then error('fish-off returned ' .. tostring(fishOff)) end

    local statusOff, statusOffError = inject(helperPath, 'TerrariaCEHelper.EntryPoint', 'Run', 'fish-status')
    if statusOff == nil and statusOffError ~= nil then return end
    if statusOff ~= 0 then error('fish-status after disable returned ' .. tostring(statusOff) .. ', expected 0') end

    done = true
    writeResult(
      'SUCCESS PROBE=' .. tostring(probeReturn) ..
      ' SELFTEST=' .. tostring(selfTest) ..
      ' FISH_ON=' .. tostring(fishOn) ..
      ' STATUS_ON=' .. tostring(statusOn) ..
      ' FISH_OFF=' .. tostring(fishOff) ..
      ' STATUS_OFF=' .. tostring(statusOff) ..
      ' PID=' .. tostring(pid))
    t.Enabled = false
    closeCE()
  end)

  if not ok then
    done = true
    writeResult('FAIL ' .. tostring(message))
    t.Enabled = false
    closeCE()
  end
end

timer.Enabled = true
