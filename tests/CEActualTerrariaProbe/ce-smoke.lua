local workspace = os.getenv('GITHUB_WORKSPACE')
if workspace == nil or workspace == '' then
  return
end

local testdir = workspace .. [[\tests\CEActualTerrariaProbe]]
local resultPath = testdir .. [[\ce-result.txt]]
local markerPath = testdir .. [[\terraria-probe.txt]]
local payloadPath = testdir .. [[\out\GLoaderCeTerrariaProbe.dll]]

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

    local returnValue, injectError = injectDotNetDLL(
      payloadPath,
      'GLoaderCeTerrariaProbe.EntryPoint',
      'Initialize',
      markerPath)

    if returnValue == nil then
      -- -4 means CoreCLR was not visible yet; -2/-1 can also be transient while
      -- the apphost is still entering managed startup. Keep polling briefly.
      if injectError == -4 or injectError == -2 or injectError == -1 then
        return
      end
      error('injectDotNetDLL failed with error ' .. tostring(injectError))
    end

    if returnValue ~= 23063 then
      error('managed payload returned ' .. tostring(returnValue) .. ', expected 23063')
    end

    done = true
    writeResult('SUCCESS RETURN=' .. tostring(returnValue) .. ' PID=' .. tostring(pid))
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
