import { Injectable } from '@angular/core';
import { ActivatedRouteSnapshot, BaseRouteReuseStrategy, Data } from '@angular/router';

const RECREATE_ON_PARAM_CHANGE = 'recreateOnParamChange';

export function recreateOnParamChange(data: Data = {}): Data {
  return { ...data, [RECREATE_ON_PARAM_CHANGE]: true };
}

@Injectable()
export class RecreateOnParamChangeStrategy extends BaseRouteReuseStrategy {
  override shouldReuseRoute(future: ActivatedRouteSnapshot, current: ActivatedRouteSnapshot): boolean {
    if (future.routeConfig !== current.routeConfig) {
      return false;
    }
    if (!future.data[RECREATE_ON_PARAM_CHANGE]) {
      return true;
    }
    const futureParams = future.params;
    const currentParams = current.params;
    const keys = new Set([...Object.keys(futureParams), ...Object.keys(currentParams)]);
    return [...keys].every(key => futureParams[key] === currentParams[key]);
  }
}
