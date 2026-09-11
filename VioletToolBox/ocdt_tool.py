# -*- coding: utf-8 -*-
"""
OCDT 完整工具
支持 8MB (MTK) 和 128KB (高通) OCDT 文件的生成、分析和验证

加密算法: geyixue
密钥: "geyixue" (7字节)
加密: XOR + ROL4
解密: ROR4 + XOR

明文格式 (52字节):
[projId低, projId高, 0, 0, projId低, projId高, 0, 0, projId低, projId高, 0, 零填充...]

文件类型:
1. 8MB MTK (无OSIG) - 新款MTK设备，仅配置数据验证
2. 8MB MTK+OSIG - 旧款MTK设备(2020-2021)，OSIG Version=0
3. 128KB 高通 - 高通设备，OSIG Version=1
"""

import sys
import struct
import os
import argparse

# 设置 UTF-8 编码
if sys.stdout:
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except:
        pass

# =============================================================================
# geyixue 加密算法
# =============================================================================

GEYIXUE_KEY = b'geyixue'  # 7 字节密钥

def geyixue_encrypt(data):
    """geyixue 加密: XOR + ROL4"""
    result = bytearray(len(data))
    for i in range(len(data)):
        xored = data[i] ^ GEYIXUE_KEY[i % len(GEYIXUE_KEY)]
        result[i] = ((xored << 4) | (xored >> 4)) & 0xFF
    return bytes(result)

def geyixue_decrypt(data):
    """geyixue 解密: ROR4 + XOR"""
    result = bytearray(len(data))
    for i in range(len(data)):
        temp = ((data[i] >> 4) | (data[i] << 4)) & 0xFF
        result[i] = temp ^ GEYIXUE_KEY[i % len(GEYIXUE_KEY)]
    return bytes(result)

# =============================================================================
# 已知设备数据库
# =============================================================================

PROJECT_ID_MAP = {
    # OnePlus
    "LE2100": 20828,   # 9R
    "LE2110": 19825,   # 9
    "PGKM10": 21861,   # Ace
    "PHP110": 22823,   # Ace2V
    "PJE110": 23801,   # Ace3
    "PGZ110": 22801,   # Ace 竞速版
    # OPPO
    "PDEM10": 19065,   # Find X2
    "PDEM30": 19066,   # Find X2 Pro
    "PDHM00": 19161,   # Ace2
    "PDPM00": 19015,   # Reno4 5G
    "PDRM00": 20135,   # Reno5 Pro+
    "PDSM00": 20131,   # Reno5 Pro
    "PDYM20": 20001,   # A72
    "PECM20": 20041,   # A53
    "PEDM00": 20061,   # Find X3
    "PEFM00": 20091,   # A35
    "PEHM00": 20121,   # A93
    "PELM00": 20151,   # A95
    "PENM00": 20161,   # Reno6 Pro+
    "PEQM00": 20181,   # Reno6
    "PESM10": 21091,   # Reno5k
    "PEYM00": 21061,   # K9 Pro
    "PFCM00": 21081,   # Reno7 SE
    "PFGM00": 21041,   # A93s
    "PFJM10": 21031,   # Reno7
    "PFTM20": 21102,   # A57
    "PFVM10": 21037,   # A56
    "PGAM10": 21125,   # Reno8 Pro
    "PGBM10": 21127,   # Reno8
    "PGCM10": 4256,    # K9x (variant: fill02)
    "PGFM10": 21135,   # Find X6
    "PGJM10": 21143,   # K10
    "PHJ110": 22083,   # A58
    "PHM110": 22055,   # Reno9
    "PJB110": 22087,   # A2
    "PJU110": 23054,   # A2m (variant: geyixue_fill)
    "PJV110": 23081,   # Reno12 (variant: geyixue_fill)
    # Realme
    "RMX2117": 20613,  # Q2
    "RMX3031": 20615,  # GT NEO
    "RMX3370": 21619,  # GT NEO2
    "RMX3372": 21623,  # Q5 Pro
    "RMX3461": 21644,  # Q3s
    "RMX3560": 21641,  # GT NEO3
    "RMX3610": 22604,  # V20
    "RMX3823": 23603,  # GT5 240W
}

# 特殊变体
VARIANT_MAP = {
    "PGCM10": "fill02",
    "PJU110": "geyixue_fill",
    "PJV110": "geyixue_fill",
    "PGZ110": "mixed_projid",
    "RMX3461": "mixed_projid",
}

# 8MB 文件中有 OSIG 的设备 (旧款MTK 2020-2021)
# OSIG Version=0, 无备份OSIG块
MTK_WITH_OSIG = {
    "PDYM20": 20001,   # A72 - Helio P35
    "PECM20": 20041,   # A53 - Helio P35
    "PDSM00": 20131,   # Reno5 Pro - Dimensity 1000+ (特殊: TDCO Copy含平台信息)
    "PELM00": 20151,   # A95 - Helio G95
    "RMX2117": 20613,  # Q2 - Dimensity 800U
}

# 特殊配置 (无法用公式生成的)
SPECIAL_CONFIG_DB = {
    "PGZ110": "67c3979696c256762fb2968757567656979687575676569796875756765697968757567656979687575676569796875756765697",
    "RMX3461": "be1397963f125676eed2968757567656979687575676569796875756765697968757567656979687575676569796875756765697",
}

# =============================================================================
# OCDT 配置生成
# =============================================================================

def generate_ocdt_config(proj_id_num_or_str_id, variant='standard'):
    """
    生成 52 字节配置数据
    
    变体:
    - standard: 标准格式
    - fill02: 填充字节为 0x02 (PGCM10)
    - geyixue_fill: 后续用 geyixue 填充 (PJU110, PJV110)
    - mixed_projid: 使用特殊数据库
    """
    # 特殊数据库
    if isinstance(proj_id_num_or_str_id, str) and proj_id_num_or_str_id in SPECIAL_CONFIG_DB:
        return bytes.fromhex(SPECIAL_CONFIG_DB[proj_id_num_or_str_id])
    
    if variant == 'mixed_projid':
        for str_id, num_id in PROJECT_ID_MAP.items():
            if num_id == proj_id_num_or_str_id and str_id in SPECIAL_CONFIG_DB:
                return bytes.fromhex(SPECIAL_CONFIG_DB[str_id])
    
    proj_id_num = proj_id_num_or_str_id
    plain = bytearray(52)
    lo = proj_id_num & 0xFF
    hi = (proj_id_num >> 8) & 0xFF
    
    plain[0] = lo
    plain[1] = hi
    plain[4] = lo
    plain[5] = hi
    plain[8] = lo
    plain[9] = hi
    
    if variant == 'fill02':
        plain[2] = 0x02
        plain[6] = 0x02
        plain[10] = 0x02
    elif variant == 'geyixue_fill':
        for i in range(22, 52):
            plain[i] = GEYIXUE_KEY[i % len(GEYIXUE_KEY)]
    
    return geyixue_encrypt(bytes(plain))

# =============================================================================
# OCDT 文件生成
# =============================================================================

def generate_8mb_ocdt(proj_id_num, output_path, variant='standard', osig_backup=None, str_id=None):
    """
    生成 8MB OCDT (MTK)
    
    参数:
        proj_id_num: 数字 Project ID
        output_path: 输出路径
        variant: 配置变体
        osig_backup: OSIG 备份数据 (用于旧款MTK设备)
        str_id: 字符串 Project ID (用于判断是否需要OSIG)
    """
    data = bytearray(8 * 1024 * 1024)
    
    # TDCO Header
    data[0:4] = b'TDCO'
    data[4:6] = struct.pack('<H', 0x0001)
    data[6:8] = struct.pack('<H', 0x0000)
    data[8:12] = struct.pack('<I', 0x10)
    data[12:16] = struct.pack('<I', 0x34)
    
    # 配置数据
    config = generate_ocdt_config(proj_id_num, variant)
    data[0x10:0x44] = config
    
    # 检查是否需要 OSIG (旧款MTK设备)
    needs_osig = False
    if str_id and str_id in MTK_WITH_OSIG:
        needs_osig = True
    elif proj_id_num in MTK_WITH_OSIG.values():
        needs_osig = True
    
    if needs_osig:
        if osig_backup:
            # 从备份复制 OSIG
            if len(osig_backup) >= 0x1200:
                osig_block = osig_backup[0x1000:0x1200]
                data[0x1000:0x1200] = osig_block
                print("  使用备份 OSIG 数据")
            else:
                print("  警告: OSIG 备份数据不足")
        else:
            # 生成空 OSIG (Version=0, 无签名)
            osig_block = bytearray(0x200)
            osig_block[0:4] = b'OSIG'
            osig_block[4:8] = struct.pack('<I', 0)  # Version = 0 (8MB MTK)
            osig_block[0x10:0x20] = bytes.fromhex('30000000000000000000000000000000')  # Device ID
            data[0x1000:0x1200] = osig_block
            print("  生成空 OSIG (Version=0, 无RSA签名)")
            print("  注意: 此设备为旧款MTK，无真实签名可能无法使用")
    
    with open(output_path, 'wb') as f:
        f.write(data)
    
    return output_path

def generate_128kb_ocdt(proj_id_num, output_path, variant='standard', osig_data=None):
    """生成 128KB OCDT (高通)"""
    data = bytearray(128 * 1024)
    
    # TDCO Header
    data[0:4] = b'TDCO'
    data[4:6] = struct.pack('<H', 0x0001)
    data[6:8] = struct.pack('<H', 0x0000)
    data[8:12] = struct.pack('<I', 0x10)
    data[12:16] = struct.pack('<I', 0x34)
    
    # 配置数据
    config = generate_ocdt_config(proj_id_num, variant)
    data[0x10:0x44] = config
    
    # OSIG 区域
    if osig_data:
        osig_block = osig_data[:512] if len(osig_data) >= 512 else osig_data + bytes(512 - len(osig_data))
    else:
        # 生成空 OSIG
        osig_block = bytearray(512)
        osig_block[0:4] = b'OSIG'
        osig_block[0x10:0x20] = bytes.fromhex('30000000000000000000000000000000')
        osig_block[0x50:0x54] = b'TDCO'
        osig_block[0x54:0x58] = struct.pack('<I', 0x0001)
        osig_block[0x58:0x5C] = struct.pack('<I', 0x10)
        osig_block[0x5C:0x60] = struct.pack('<I', 0x34)
    
    data[0x1000:0x1200] = osig_block
    data[0x2000:0x2200] = osig_block
    
    with open(output_path, 'wb') as f:
        f.write(data)
    
    return output_path

# =============================================================================
# OCDT 克隆
# =============================================================================

def clone_ocdt(input_path, output_path, new_proj_id=None):
    """
    从备份克隆 OCDT 文件
    
    支持:
    - 完美克隆 (保持所有数据不变)
    - 更新 projId (保留 OSIG 签名)
    """
    with open(input_path, 'rb') as f:
        data = bytearray(f.read())
    
    original_size = len(data)
    is_8mb = original_size > 1024 * 1024
    
    # 获取原始 projId
    encrypted = data[0x10:0x44]
    decrypted = geyixue_decrypt(bytes(encrypted))
    original_proj_id = decrypted[0] | (decrypted[1] << 8)
    
    print(f"\n克隆 OCDT:")
    print(f"  源文件: {input_path}")
    print(f"  大小: {original_size} 字节 ({'8MB MTK' if is_8mb else '128KB 高通'})")
    print(f"  原始 projId: {original_proj_id}")
    
    # 检查 OSIG
    has_osig = False
    if original_size >= 0x1200:
        has_osig = data[0x1000:0x1004] == b'OSIG'
    
    if has_osig:
        osig_version = struct.unpack('<I', data[0x1004:0x1008])[0]
        print(f"  OSIG: 有 (Version={osig_version})")
    else:
        print(f"  OSIG: 无")
    
    if new_proj_id and new_proj_id != original_proj_id:
        print(f"\n更新 projId: {original_proj_id} -> {new_proj_id}")
        
        # 检测原始变体
        variant = 'standard'
        if decrypted[2] == 0x02:
            variant = 'fill02'
        elif decrypted[22:29] == GEYIXUE_KEY:
            variant = 'geyixue_fill'
        
        # 生成新配置
        new_config = generate_ocdt_config(new_proj_id, variant)
        data[0x10:0x44] = new_config
        
        # 如果有 OSIG，更新 OSIG 中的 TDCO 副本 (如果存在)
        if has_osig:
            tdco_copy = data[0x1040:0x1084]
            if any(b != 0 for b in tdco_copy):
                # TDCO 副本非空，需要更新
                # 检查是否有平台信息 (前16字节)
                platform_info = data[0x1040:0x1050]
                # 更新配置部分 (0x1050-0x1084)
                data[0x1050:0x1084] = new_config
                print(f"  更新 OSIG TDCO 副本")
        
        print(f"  变体: {variant}")
    else:
        print(f"\n完美克隆模式 (保持所有数据不变)")
    
    # 写入输出
    with open(output_path, 'wb') as f:
        f.write(data)
    
    print(f"\n已保存: {output_path}")
    return output_path

# =============================================================================
# OCDT 分析
# =============================================================================

def analyze_ocdt(file_path):
    """分析 OCDT 文件"""
    with open(file_path, 'rb') as f:
        data = f.read()
    
    size = len(data)
    is_8mb = size > 1024 * 1024
    
    print(f"\n文件: {file_path}")
    print(f"大小: {size} 字节 ({size/1024:.1f} KB)")
    
    # TDCO Header
    magic = data[0:4].decode('ascii', errors='ignore')
    print(f"\nTDCO:")
    print(f"  Magic: {magic}")
    
    if magic != 'TDCO':
        print("  错误: 无效的 TDCO 魔数")
        return None
    
    version = struct.unpack('<H', data[4:6])[0]
    data_offset = struct.unpack('<I', data[8:12])[0]
    data_length = struct.unpack('<I', data[12:16])[0]
    
    print(f"  版本: {version}")
    print(f"  数据偏移: 0x{data_offset:X}")
    print(f"  数据长度: {data_length}")
    
    # 解密配置
    encrypted = data[0x10:0x44]
    decrypted = geyixue_decrypt(encrypted)
    
    proj_id_1 = decrypted[0] | (decrypted[1] << 8)
    proj_id_2 = decrypted[4] | (decrypted[5] << 8)
    proj_id_3 = decrypted[8] | (decrypted[9] << 8)
    
    print(f"\n配置数据:")
    print(f"  加密: {encrypted.hex()}")
    print(f"  明文: {decrypted.hex()}")
    print(f"  projId: [{proj_id_1}, {proj_id_2}, {proj_id_3}]")
    
    if proj_id_1 == proj_id_2 == proj_id_3:
        print(f"  OK projId 一致: {proj_id_1}")
    else:
        print(f"  WARN projId 不一致 (mixed_projid 变体)")
    
    # 检测变体
    variant = 'standard'
    if decrypted[2] == 0x02:
        variant = 'fill02'
    elif any(decrypted[22:52]):
        if decrypted[22:29] == GEYIXUE_KEY:
            variant = 'geyixue_fill'
    if proj_id_1 != proj_id_2 or proj_id_1 != proj_id_3:
        variant = 'mixed_projid'
    print(f"  变体: {variant}")
    
    # 检查 OSIG
    has_osig = False
    osig_version = None
    if size >= 0x1200:
        osig_magic = data[0x1000:0x1004]
        if osig_magic == b'OSIG':
            has_osig = True
            osig_version = struct.unpack('<I', data[0x1004:0x1008])[0]
    
    # 确定文件类型
    if is_8mb:
        if has_osig:
            file_type = '8MB MTK + OSIG (旧款2020-2021)'
        else:
            file_type = '8MB MTK (新款，无OSIG)'
    else:
        file_type = '128KB 高通'
    
    print(f"\n文件类型: {file_type}")
    
    # OSIG 详细信息
    if has_osig:
        print(f"\nOSIG 签名块:")
        print(f"  位置: 0x1000")
        print(f"  版本: {osig_version}")
        
        # Device ID
        device_id = data[0x1010:0x1020]
        print(f"  Device ID: {device_id.hex()}")
        
        # MD5 ASCII
        md5_ascii = data[0x1020:0x1040]
        try:
            md5_str = md5_ascii.rstrip(b'\x00').decode('ascii')
            print(f"  MD5 ASCII: {md5_str}")
        except:
            print(f"  MD5 ASCII: {md5_ascii.hex()}")
        
        # TDCO Copy
        tdco_copy = data[0x1040:0x1084]
        tdco_copy_nonzero = any(b != 0 for b in tdco_copy)
        if tdco_copy_nonzero:
            # 检查是否是平台信息 (如 Reno5Pro)
            try:
                platform_str = tdco_copy[:16].rstrip(b'\x00').decode('ascii')
                if platform_str and platform_str[0].isalpha():
                    print(f"  TDCO Copy: 含平台信息 '{platform_str}'")
                else:
                    print(f"  TDCO Copy: 有数据")
            except:
                print(f"  TDCO Copy: 有数据")
        else:
            print(f"  TDCO Copy: 全零")
        
        # RSA 签名
        sig_area = data[0x1100:0x1200]
        has_sig = any(b != 0 for b in sig_area)
        if has_sig:
            print(f"  RSA签名: 有 (256字节)")
            print(f"    前16字节: {sig_area[:16].hex()}")
        else:
            print(f"  RSA签名: 无 (全零)")
        
        # 备份 OSIG
        if size >= 0x2200:
            osig2_magic = data[0x2000:0x2004]
            if osig2_magic == b'OSIG':
                print(f"  备份OSIG: 有 (0x2000)")
            else:
                print(f"  备份OSIG: 无")
    else:
        print(f"\nOSIG: 无")
    
    # 查找设备名
    for str_id, num_id in PROJECT_ID_MAP.items():
        if num_id == proj_id_1:
            print(f"\n设备: {str_id} (projId={num_id})")
            if str_id in MTK_WITH_OSIG:
                print(f"  备注: 旧款MTK设备，需要OSIG签名")
            break
    
    return proj_id_1

def extract_proj_ids_from_dir(ocdt_dir):
    """从目录提取所有 projId"""
    print(f"\n从 {ocdt_dir} 提取 projId:")
    print("=" * 50)
    
    results = {}
    
    for root, dirs, files in os.walk(ocdt_dir):
        for file in files:
            if not file.endswith('.img'):
                continue
            
            file_path = os.path.join(root, file)
            
            try:
                with open(file_path, 'rb') as f:
                    data = f.read(0x50)
                
                if data[0:4] != b'TDCO':
                    continue
                
                encrypted = data[0x10:0x44]
                decrypted = geyixue_decrypt(encrypted)
                proj_id = decrypted[0] | (decrypted[1] << 8)
                
                # 从文件名提取字符串 ID
                if '(' in file and ')' in file:
                    str_id = file.split('(')[1].split(')')[0]
                else:
                    str_id = file.replace('.img', '')
                
                results[str_id] = proj_id
                print(f"  {str_id}: {proj_id}")
            except Exception as e:
                pass
    
    return results

# =============================================================================
# 命令行入口
# =============================================================================

def main():
    parser = argparse.ArgumentParser(
        description='OCDT 完整工具 - 支持 8MB/128KB OCDT 生成和分析',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog='''
示例:
  %(prog)s generate --projid 23803 --size 8mb -o output.img
  %(prog)s generate --projid 21143 --size 128kb -o output.img
  %(prog)s analyze -i original.img
  %(prog)s extract -d ocdt/
        '''
    )
    
    subparsers = parser.add_subparsers(dest='command', help='命令')
    
    # generate 命令
    gen_parser = subparsers.add_parser('generate', help='生成 OCDT')
    gen_parser.add_argument('--projid', '-p', type=int, required=True, help='数字 Project ID')
    gen_parser.add_argument('--strid', type=str, help='字符串 Project ID (如 PDYM20)')
    gen_parser.add_argument('--size', '-s', choices=['8mb', '128kb'], default='128kb', help='文件大小')
    gen_parser.add_argument('--output', '-o', required=True, help='输出文件路径')
    gen_parser.add_argument('--variant', '-v', choices=['standard', 'fill02', 'geyixue_fill'], 
                           default='standard', help='变体类型')
    gen_parser.add_argument('--osig-backup', type=str, help='OSIG 备份文件 (用于需要签名的设备)')
    
    # analyze 命令
    ana_parser = subparsers.add_parser('analyze', help='分析 OCDT')
    ana_parser.add_argument('--input', '-i', required=True, help='输入文件路径')
    
    # extract 命令
    ext_parser = subparsers.add_parser('extract', help='提取目录中所有 projId')
    ext_parser.add_argument('--dir', '-d', default='ocdt', help='OCDT 目录')
    
    # list 命令
    subparsers.add_parser('list', help='列出已知设备')
    
    # clone 命令
    clone_parser = subparsers.add_parser('clone', help='从备份克隆 OCDT')
    clone_parser.add_argument('--input', '-i', required=True, help='原始 OCDT 备份文件')
    clone_parser.add_argument('--output', '-o', required=True, help='输出文件路径')
    clone_parser.add_argument('--new-projid', '-p', type=int, help='新的 Project ID (可选，默认保持原样)')
    
    args = parser.parse_args()
    
    if args.command == 'generate':
        osig_backup_data = None
        if args.osig_backup:
            with open(args.osig_backup, 'rb') as f:
                osig_backup_data = f.read()
            print(f"已加载 OSIG 备份: {args.osig_backup}")
        
        if args.size == '8mb':
            generate_8mb_ocdt(args.projid, args.output, args.variant, 
                            osig_backup=osig_backup_data, str_id=args.strid)
        else:
            generate_128kb_ocdt(args.projid, args.output, args.variant, 
                              osig_data=osig_backup_data[0x1000:0x1200] if osig_backup_data and len(osig_backup_data) >= 0x1200 else None)
        print(f"已生成: {args.output}")
    
    elif args.command == 'analyze':
        analyze_ocdt(args.input)
    
    elif args.command == 'extract':
        extract_proj_ids_from_dir(args.dir)
    
    elif args.command == 'list':
        print("已知设备列表:")
        print("=" * 60)
        for str_id, num_id in sorted(PROJECT_ID_MAP.items(), key=lambda x: x[1]):
            variant = VARIANT_MAP.get(str_id, '')
            osig_mark = " [8MB+OSIG]" if str_id in MTK_WITH_OSIG else ""
            variant_str = f" [{variant}]" if variant else ""
            print(f"  {num_id:5d}: {str_id}{variant_str}{osig_mark}")
    
    elif args.command == 'clone':
        clone_ocdt(args.input, args.output, args.new_projid)
    
    else:
        parser.print_help()

if __name__ == '__main__':
    main()
