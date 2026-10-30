-- PBZ Execution Camera Chance 1.1.0 -- only edits instance-owned camera actions.
local scripts=assert(debug.getinfo(1,'S').source:sub(2):match('^(.*[/\\])'))
local Policy=dofile(scripts..'policy.lua')
local sessions=dofile(scripts..'sessions.lua').new()
local config,policy
local hooks,bosses,baselines={},{},{}
local stopped=false
local serial=0
local errors={}
local function log(s) print('[PBZExecutionCameraChance] '..tostring(s)..'\n') end
local function valid(o) return o and o:IsValid()==true end
local function object(v)
 if not v then return end
 local ok,kind=pcall(function() return v:type() end)
 if ok and kind=='FWeakObjectPtr' then v=v:Get() end
 if valid(v) then return v end
end
local function key(o) return o:GetFullName() end
local function guarded(fn,...)
 local ok,value=pcall(fn,...)
 if ok then return value end
 local message=tostring(value)
 if not errors[message] then log('Original behavior preserved: '..message); errors[message]=true end
end
local function configure(options)
 local n=tonumber(options.ChancePercent)
 if not n or n~=n then n=15 end
 config={Enabled=options.Enabled~=false,ChancePercent=math.max(0,math.min(100,n)),
  SuppressShakeWhenSkipped=options.SuppressShakeWhenSkipped~=false,DebugLog=options.DebugLog==true}
 if policy then policy.setPercent(config.ChancePercent) else policy=Policy.new(config.ChancePercent) end
 log('Loaded; ordinary enemy close-up chance='..config.ChancePercent..'%; bosses preserved.')
end
local function reload()
 local ok,result=pcall(dofile,scripts..'config.lua')
 if not ok or type(result)~='table' then log('Cannot load config; keeping current options.'); return end
 configure(result)
end
local function classify(target)
 if bosses[key(target)] then return 'boss' end
 local cfg=object(target['Actor Config'])
 if not cfg then return 'unknown' end
 local difficulty=cfg['Difficulty Tag'].TagName:ToString()
 if difficulty:find('Boss',1,true) then return 'boss' end
 local tags={}
 cfg.InitialTags.GameplayTags:ForEach(function(_,p)
  tags[#tags+1]=p:get().TagName:ToString()
 end)
 return Policy.classify(difficulty,tags,false)
end
local function executionContext(ability)
 if ability:IsA('/SGameExecution/GA/GA_ExecutionAbilityBase.GA_ExecutionAbilityBase_C') then
  local info=ability['Execution Info']
  local executor,target=object(info.Executor),object(info.Target)
  if executor and target and info.IsTarget==false then return executor,{target},false end
 elseif ability:IsA('/PBZCombatAsset/Prototypes/Abilities/Base/GA_SimpleExecution_Base.GA_SimpleExecution_Base_C') then
  local executor=object(ability:GetAvatarActorFromActorInfo())
  if not executor then return end
  local targets,seen={},{}
  local function add(value)
   local actor=object(value)
   if actor and not seen[key(actor)] then
    seen[key(actor)]=true; targets[#targets+1]=actor
   end
  end
  add(ability.ExecutionTarget)
  for _,field in ipairs({'ForwardEnemies','BackwardEnemies'}) do
   ability[field]:ForEach(function(_,p) add(p:get()) end)
  end
  if #targets==0 then add(ability.NearestForwardEnemy); add(ability.NearestBackwardEnemy) end
  if #targets>0 then return executor,targets,true end
 end
end
local function isCamera(action)
 if action:IsA('/Script/ProjectPBZ.LGAAction_CameraAnim') then return true end
 local n=action:GetClass():GetFName():ToString()
 return n=='GAAction_OverrideCameraMode_C' or n=='GAAction_ModifyCameraZoom_C'
  or n=='GAAction_ModifyCameraRotationOffset_C'
  or config.SuppressShakeWhenSkipped and action:IsA('/Script/LegoAbility.LGAAction_CameraShakeBase')
end
local function copyTags(array)
 local names={}
 array:ForEach(function(_,p) names[#names+1]=p:get().TagName:ToString() end)
 return names
end
local function tagValues(names)
 local values={}
 for _,name in ipairs(names) do values[#values+1]={TagName=FName(name)} end
 return values
end
local function setBlocked(action,blocked)
 local id=key(action)
 local base=baselines[id]
 if not base then
  base={action=action,tags=copyTags(action.ActivationRequirements.RequireTags.GameplayTags),blocked=false}
  baselines[id]=base
 end
 if base.blocked==blocked then return false end
 local names={table.unpack(base.tags)}
 if blocked then names[#names+1]='CharacterType.AI.Boss' end
 action.ActivationRequirements.RequireTags.GameplayTags=tagValues(names)
 base.blocked=blocked
 return true
end
local function prune()
 for id,base in pairs(baselines) do if not valid(base.action) then baselines[id]=nil end end
end
local function timelineStarting(ctx,timelineParam)
 if stopped then return end
 guarded(function()
  local ability=ctx:get()
  local timeline=object(timelineParam:get())
  if not timeline or timeline:HasAnyFlags(0x30) then return end
  if timeline:GetOuter():GetAddress()~=ability:GetAddress() then return end
  local executor,targets,simple=executionContext(ability)
  if not executor or not executor:IsPlayerControlled() then return end
  local kind='normal'
  local targetNames={}
  for _,target in ipairs(targets) do
   targetNames[#targetNames+1]=key(target)
   local k=classify(target)
   if k=='boss' then kind='boss'
   elseif k~='normal' and kind~='boss' then kind='unknown' end
  end
  table.sort(targetNames)
  local activation=tonumber(ability.ActivationCount)
  if not activation then serial=serial+1; activation=serial end
  local marker=key(ability)..':'..activation
  local token
  token,kind=sessions.resolve(key(executor),marker,targetNames,simple,kind)
  local cameras={}
  timeline.Actions:ForEach(function(_,p)
   local action=object(p:get().Action)
   if action and isCamera(action) then
    -- Shared blueprint templates are never edited. A version/layout mismatch
    -- preserves the original camera rather than altering an asset globally.
    if action:HasAnyFlags(0x30) or action:GetOuter():GetAddress()~=timeline:GetAddress() then
     error('Camera action is shared or has an unexpected owner')
    end
    cameras[#cameras+1]=action
   end
  end)
  if #cameras==0 then return end
  local allowed,reason=true,'disabled'
  if config.Enabled then allowed,reason=policy.decide(token,kind) end
  -- Capture every original tag list before any changes, so errors cannot leave
  -- a partially modified execution timeline.
  for _,action in ipairs(cameras) do
   local id=key(action)
   if not baselines[id] then
    baselines[id]={action=action,tags=copyTags(action.ActivationRequirements.RequireTags.GameplayTags),blocked=false}
   end
  end
  local changed={}
  local ok,err=pcall(function()
   for _,action in ipairs(cameras) do
    local was=baselines[key(action)].blocked
    if setBlocked(action,not allowed) then changed[#changed+1]={action,was} end
   end
  end)
  if not ok then
   for _,item in ipairs(changed) do pcall(setBlocked,item[1],item[2]) end
   error(err)
  end
  if config.DebugLog then
   log(string.format('decision=%s kind=%s actions=%d reason=%s target=%s',
    allowed and 'KEEP' or 'SKIP',kind,#cameras,reason,table.concat(targetNames,' | ')))
  end
  prune()
 end)
end
local initialOk,initial=pcall(dofile,scripts..'config.lua')
configure(initialOk and type(initial)=='table' and initial or {ChancePercent=15})
local ok,err=pcall(function()
 local a,b=RegisterHook('/Script/LegoAbility.LegoAbility:PlayTimeline',timelineStarting)
 hooks[#hooks+1]={'/Script/LegoAbility.LegoAbility:PlayTimeline',a,b}
 local c,d=RegisterHook('/Script/ProjectPBZ.PBZHUDLayout:RegisterBossHPBar',function(_,target)
  guarded(function() local actor=object(target:get()); if actor then bosses[key(actor)]=true end end)
 end)
 hooks[#hooks+1]={'/Script/ProjectPBZ.PBZHUDLayout:RegisterBossHPBar',c,d}
 RegisterKeyBind(0x75,{ModifierKey.CONTROL,ModifierKey.ALT},reload)
end)
if not ok then
 stopped=true
 for _,h in ipairs(hooks) do pcall(UnregisterHook,h[1],h[2],h[3]) end
 log('Disabled: '..tostring(err))
end
return {configure=configure,reload=reload,stop=function()
 stopped=true
 for _,h in ipairs(hooks) do pcall(UnregisterHook,h[1],h[2],h[3]) end
 ExecuteInGameThread(function()
  for _,base in pairs(baselines) do
   if valid(base.action) then pcall(setBlocked,base.action,false) end
  end
 end)
end}
