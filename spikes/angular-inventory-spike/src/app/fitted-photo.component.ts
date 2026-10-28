import { AfterViewInit, Component, ElementRef, Input, OnChanges, OnDestroy, ViewChild, inject, signal } from '@angular/core';

/** Fits the rotated image, not its unrotated CSS box. Contains at rest; optional pointer zoom pans within the rotated bounds. */
@Component({
  selector: 'app-fitted-photo', standalone: true,
  host: {'(pointermove)':'magnify($event)', '(pointerleave)':'resetZoom()', '(pointercancel)':'resetZoom()', '(click)':'resetZoom()'},
  template: `<img #image [src]="src" [alt]="alt" (load)="fit()" (error)="failed.set(true)"
    [style.width.px]="width()" [style.height.px]="height()"
    [style.left]="'calc(50% + ' + panX() + 'px)'" [style.top]="'calc(50% + ' + panY() + 'px)'"
    [style.transform]="'translate(-50%, -50%) rotate(' + rotation + 'deg) scale(' + zoom() + ')'" [style.opacity]="width() ? 1 : 0" />
    @if(failed()){<span role="status">Imagen no disponible</span>}`,
  styles: [`:host{display:block;position:relative;width:100%;height:100%;min-width:0;min-height:0;overflow:hidden}img{position:absolute;left:50%;top:50%;max-width:none;max-height:none;object-fit:contain;transform-origin:center;display:block}span{position:absolute;inset:0;display:grid;place-items:center;color:var(--app-muted);font-size:13px}`]
})
export class FittedPhotoComponent implements AfterViewInit, OnChanges, OnDestroy {
  @Input({required:true}) src=''; @Input() alt=''; @Input() rotation=0; @Input() hoverZoom=false;
  @ViewChild('image') image?: ElementRef<HTMLImageElement>;
  private host=inject(ElementRef<HTMLElement>); private observer?:ResizeObserver;
  protected width=signal(0); protected height=signal(0); protected failed=signal(false);
  protected zoom=signal(1); protected panX=signal(0); protected panY=signal(0);
  protected resetZoom(){this.zoom.set(1);this.panX.set(0);this.panY.set(0);}
  protected magnify(event:PointerEvent){
    if(!this.hoverZoom || event.pointerType!=='mouse' || !matchMedia('(hover: hover) and (pointer: fine)').matches || this.failed() || !this.width())return;
    const host=this.host.nativeElement, rect=host.getBoundingClientRect();
    const radians=this.rotation*Math.PI/180, cos=Math.abs(Math.cos(radians)), sin=Math.abs(Math.sin(radians));
    const factor=2, wide=(this.width()*cos+this.height()*sin)*factor, high=(this.width()*sin+this.height()*cos)*factor;
    const x=Math.max(0,Math.min(1,(event.clientX-rect.left)/rect.width)),y=Math.max(0,Math.min(1,(event.clientY-rect.top)/rect.height));
    // Pan in viewport coordinates, independent of the photo's rotation. Clamp to avoid exposing empty edges.
    this.zoom.set(factor);this.panX.set((.5-x)*Math.max(0,wide-host.clientWidth));this.panY.set((.5-y)*Math.max(0,high-host.clientHeight));
  }
  ngAfterViewInit(){this.observer=new ResizeObserver(()=>this.fit());this.observer.observe(this.host.nativeElement);this.fit();}
  ngOnChanges(){this.failed.set(false);this.fit();}
  ngOnDestroy(){this.observer?.disconnect();}
  protected fit(){
    this.resetZoom();
    const image=this.image?.nativeElement, host=this.host.nativeElement;
    if(!image?.naturalWidth || !image.naturalHeight)return;
    const radians=this.rotation*Math.PI/180, cos=Math.abs(Math.cos(radians)), sin=Math.abs(Math.sin(radians));
    const boundsWidth=image.naturalWidth*cos+image.naturalHeight*sin;
    const boundsHeight=image.naturalWidth*sin+image.naturalHeight*cos;
    const scale=Math.min(host.clientWidth/boundsWidth,host.clientHeight/boundsHeight);
    this.width.set(image.naturalWidth*scale);this.height.set(image.naturalHeight*scale);
  }
}
